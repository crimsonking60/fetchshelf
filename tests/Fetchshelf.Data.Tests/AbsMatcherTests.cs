using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.Tests;

public sealed class AbsMatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-abs-matcher-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static AbsClient CreateAbsClient(
        List<string> calledPaths,
        IReadOnlyList<(string Id, string FullPath)> libraryFolders,
        IReadOnlyDictionary<string, (string Id, string Name)> authorByQuery,
        IReadOnlyDictionary<string, (string Id, string Path)> itemByAuthorId,
        Action<HttpRequestMessage>? onRequest = null)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            onRequest?.Invoke(request);
            calledPaths.Add(request.RequestUri!.AbsolutePath + request.RequestUri.Query);
            var path = request.RequestUri.AbsolutePath;

            if (path == "/api/libraries")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        libraries = new[]
                        {
                            new { id = "lib1", name = "Audiobooks", folders = libraryFolders.Select(f => new { id = f.Id, fullPath = f.FullPath }).ToArray() },
                        },
                    }),
                };
            }

            if (path.Contains("/search"))
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["q"] ?? "";
                var authors = authorByQuery.TryGetValue(query, out var author)
                    ? new[] { new { id = author.Id, name = author.Name } }
                    : [];
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { authors }) };
            }

            if (path.StartsWith("/api/authors/"))
            {
                var authorId = path["/api/authors/".Length..];
                var items = itemByAuthorId.TryGetValue(authorId, out var item)
                    ? new[] { new { id = item.Id, path = item.Path } }
                    : [];
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = authorId, libraryItems = items }) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return new AbsClient(new HttpClient(handler), new AbsClientOptions { BaseUrl = "http://abs.local", Token = "token" });
    }

    private static async Task<TorrentEntity> SeedTorrentAsync(
        FetchshelfDbContext context, string libraryPath, IReadOnlyList<string>? authors = null, string? absId = null, string id = "hash1")
    {
        var metadata = OrganizerTestSupport.BuildMetadata(authors: authors);
        var entity = new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = Core.Metadata.TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
            LibraryPath = libraryPath,
            AbsId = absId,
        };
        context.Torrents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    [Fact]
    public async Task RunAsync_CancelledPartWayThrough_KeepsTheMatchesFoundSoFar()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/One", ["Brandon Sanderson"], id: "hash1");
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Two", ["Brandon Sanderson"], id: "hash2");
        using var cancellation = new CancellationTokenSource();
        var matcher = new AbsMatcher(context, CancellingAbsClient(cancellation, cancelOnAuthorLookup: 2), new ListLogger<AbsMatcher>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => matcher.RunAsync(cancellation.Token));

        using var check = OrganizerTestSupport.CreateContext(DbPath);
        Assert.Equal("item1", check.Torrents.Single(t => t.Id == "hash1").AbsId);
        Assert.Null(check.Torrents.Single(t => t.Id == "hash2").AbsId);
    }

    [Fact]
    public async Task RunAsync_StoppedAndTheMatchesCannotBeSaved_ReportsWhatStoppedTheRun()
    {
        OrganizerTestSupport.CreateContext(DbPath).Dispose(); // creates the database
        var failingSave = new FailingSaveInterceptor();
        using var context = new FetchshelfDbContext(
            new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={DbPath}").AddInterceptors(failingSave).Options);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/One", ["Brandon Sanderson"], id: "hash1");
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Two", ["Brandon Sanderson"], id: "hash2");
        failingSave.Fail = true;
        using var cancellation = new CancellationTokenSource();
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, CancellingAbsClient(cancellation, cancelOnAuthorLookup: 2), logger);

        var exception = await Record.ExceptionAsync(() => matcher.RunAsync(cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Contains(logger.Messages(LogLevel.Warning), m => m.Contains("disk full", StringComparison.Ordinal));
    }

    /// <summary>Finds the books' author, and cancels the run on the given author lookup, as the Cancel button would.</summary>
    private static AbsClient CancellingAbsClient(CancellationTokenSource cancellation, int cancelOnAuthorLookup)
    {
        var lookups = 0;
        return CreateAbsClient(
            [],
            [("f1", "/data/audiobooks")],
            new Dictionary<string, (string, string)> { ["Brandon Sanderson"] = ("a1", "Brandon Sanderson") },
            new Dictionary<string, (string, string)> { ["a1"] = ("item1", "/data/audiobooks/Sanderson/One") },
            request =>
            {
                if (request.RequestUri!.AbsolutePath.StartsWith("/api/authors/", StringComparison.Ordinal) && ++lookups == cancelOnAuthorLookup)
                {
                    cancellation.Cancel();
                }
            });
    }

    private sealed class FailingSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("disk full") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    [Fact]
    public async Task RunAsync_TorrentPathMatchesItemPath_StoresMatchedId()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths,
            [("f1", "/data/audiobooks")],
            new Dictionary<string, (string, string)> { ["Brandon Sanderson"] = ("a1", "Brandon Sanderson") },
            new Dictionary<string, (string, string)> { ["a1"] = ("item1", "/data/audiobooks/Sanderson/Book") });
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger);

        var summary = await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal("item1", torrent.AbsId);
        Assert.Equal(new AbsMatchSummary(Matched: 1, NotFoundYet: 0, Failed: 0), summary);
        Assert.Contains(logger.Messages(LogLevel.Information), m => m.StartsWith("Matched ", StringComparison.Ordinal) && m.EndsWith(" item1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_NoLibraryFolderPrefixesTorrentPath_NoMatchAndNoSearchCall()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths,
            [("f1", "/data/ebooks")],
            new Dictionary<string, (string, string)>(),
            new Dictionary<string, (string, string)>());
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger);

        var summary = await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Null(torrent.AbsId);
        Assert.DoesNotContain(calledPaths, p => p.Contains("/search"));
        Assert.Equal(new AbsMatchSummary(Matched: 0, NotFoundYet: 0, Failed: 1), summary);
        Assert.Contains("/data/audiobooks/Sanderson/Book", Assert.Single(logger.Messages(LogLevel.Error)));
    }

    [Fact]
    public async Task RunAsync_PathMapping_TranslatesLibraryPathToAudiobookshelfPath()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/mnt/Data/media/abs/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var absClient = CreateAbsClient(
            [],
            [("f1", "/audiobooks")],
            new Dictionary<string, (string, string)> { ["Brandon Sanderson"] = ("a1", "Brandon Sanderson") },
            new Dictionary<string, (string, string)> { ["a1"] = ("item1", "/audiobooks/Sanderson/Book") });
        var pathMapping = new Dictionary<string, string> { ["/mnt/Data/media/abs/audiobooks"] = "/audiobooks" };
        var matcher = new AbsMatcher(context, absClient, new ListLogger<AbsMatcher>(), pathMapping);

        var summary = await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal("item1", torrent.AbsId);
        Assert.Equal("/mnt/Data/media/abs/audiobooks/Sanderson/Book", torrent.LibraryPath);
        Assert.Equal(new AbsMatchSummary(Matched: 1, NotFoundYet: 0, Failed: 0), summary);
    }

    [Fact]
    public async Task RunAsync_PathMappingDoesNotFit_ErrorNamesTranslatedPath()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/mnt/Data/media/abs/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var absClient = CreateAbsClient(
            [], [("f1", "/ebooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        var pathMapping = new Dictionary<string, string> { ["/mnt/Data/media/abs"] = "/media" };
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger, pathMapping);

        var summary = await matcher.RunAsync();

        Assert.Equal(new AbsMatchSummary(Matched: 0, NotFoundYet: 0, Failed: 1), summary);
        Assert.Contains("/media/audiobooks/Sanderson/Book", Assert.Single(logger.Messages(LogLevel.Error)));
    }

    [Fact]
    public async Task RunAsync_TorrentWithNoAuthors_NeverAttemptsMatch()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Book", authors: []);
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths, [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger);

        var summary = await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Null(torrent.AbsId);
        Assert.Empty(calledPaths);
        Assert.Equal(new AbsMatchSummary(Matched: 0, NotFoundYet: 0, Failed: 1), summary);
        Assert.Contains("no author", Assert.Single(logger.Messages(LogLevel.Error)));
    }

    [Fact]
    public async Task RunAsync_TorrentWithNoAuthors_RecordsItsErrorOnTheErrorsPage()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Book", authors: []);
        var absClient = CreateAbsClient(
            [], [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());

        await new AbsMatcher(context, absClient).RunAsync();

        var error = Assert.Single(context.ErroredTorrents.AsNoTracking());
        Assert.Equal((ErrorStage.Audiobookshelf, "hash1"), (error.Stage, error.Key));
        Assert.Contains("no author", error.ErrorText);
    }

    [Fact]
    public async Task RunAsync_BookThatFailedBefore_IsClearedOnceItMatches()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var unreachable = CreateAbsClient(
            [], [("f1", "/elsewhere")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        await new AbsMatcher(context, unreachable).RunAsync();
        Assert.Contains("No Audiobookshelf library has a folder containing", Assert.Single(context.ErroredTorrents.AsNoTracking()).ErrorText);

        var reachable = CreateAbsClient(
            [],
            [("f1", "/data/audiobooks")],
            new Dictionary<string, (string, string)> { ["Brandon Sanderson"] = ("a1", "Brandon Sanderson") },
            new Dictionary<string, (string, string)> { ["a1"] = ("item1", "/data/audiobooks/Sanderson/Book") });
        await new AbsMatcher(context, reachable).RunAsync();

        Assert.Empty(context.ErroredTorrents.AsNoTracking());
        Assert.Equal("item1", context.Torrents.AsNoTracking().Single().AbsId);
    }

    [Fact]
    public async Task RunAsync_TorrentAlreadyMatched_SkippedWithoutAnyAbsCall()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Book", ["Brandon Sanderson"], absId: "existing-item");
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths, [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger);

        var summary = await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal("existing-item", torrent.AbsId);
        Assert.Empty(calledPaths);
        Assert.Equal(new AbsMatchSummary(Matched: 0, NotFoundYet: 0, Failed: 0), summary);
        Assert.Equal(["No linked books are waiting to be matched in Audiobookshelf"], logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_LibraryFolderFitsButItemNotThereYet_CountsAsNotFoundYetWithWarning()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var absClient = CreateAbsClient(
            [], [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger);

        var summary = await matcher.RunAsync();

        Assert.Equal(new AbsMatchSummary(Matched: 0, NotFoundYet: 1, Failed: 0), summary);
        Assert.Single(logger.Messages(LogLevel.Warning));
        Assert.Empty(logger.Messages(LogLevel.Error));
    }

    [Fact]
    public async Task RunAsync_SeveralBooks_ListsLibrariesOnce()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"], id: "hash1");
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Other", ["Brandon Sanderson"], id: "hash2");
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths, [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());

        await new AbsMatcher(context, absClient).RunAsync();

        Assert.Single(calledPaths, p => p == "/api/libraries");
    }

    [Fact]
    public async Task RunAsync_SearchFailsForABook_LogsErrorCountsItAndCarriesOn()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var absClient = CreateFailingAbsClient(failLibraries: false);
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, absClient, logger);

        var summary = await matcher.RunAsync();

        Assert.Equal(new AbsMatchSummary(Matched: 0, NotFoundYet: 0, Failed: 1), summary);
        Assert.Contains("500", Assert.Single(logger.Messages(LogLevel.Error)));
    }

    [Fact]
    public async Task RunAsync_LibrariesCannotBeListed_LogsErrorAndFailsTheRun()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Sanderson/Book", ["Brandon Sanderson"]);
        var logger = new ListLogger<AbsMatcher>();
        var matcher = new AbsMatcher(context, CreateFailingAbsClient(failLibraries: true), logger);

        await Assert.ThrowsAsync<HttpRequestException>(() => matcher.RunAsync());

        Assert.StartsWith("Could not list the Audiobookshelf libraries", Assert.Single(logger.Messages(LogLevel.Error)), StringComparison.Ordinal);
    }

    /// <summary>Answers every request with HTTP 500, except the library list unless <paramref name="failLibraries"/>.</summary>
    private static AbsClient CreateFailingAbsClient(bool failLibraries)
    {
        var handler = new FakeHttpMessageHandler(request =>
            !failLibraries && request.RequestUri!.AbsolutePath == "/api/libraries"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        libraries = new[] { new { id = "lib1", name = "Audiobooks", folders = new[] { new { id = "f1", fullPath = "/data/audiobooks" } } } },
                    }),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        return new AbsClient(new HttpClient(handler), new AbsClientOptions { BaseUrl = "http://abs.local", Token = "token" });
    }
}
