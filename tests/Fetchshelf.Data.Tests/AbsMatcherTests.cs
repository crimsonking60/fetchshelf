using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

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
        IReadOnlyDictionary<string, (string Id, string Path)> itemByAuthorId)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
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
        FetchshelfDbContext context, string libraryPath, IReadOnlyList<string>? authors = null, string? absId = null)
    {
        var metadata = OrganizerTestSupport.BuildMetadata(authors: authors);
        var entity = new TorrentEntity
        {
            Id = "hash1",
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
        var matcher = new AbsMatcher(context, absClient);

        await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal("item1", torrent.AbsId);
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
        var matcher = new AbsMatcher(context, absClient);

        await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Null(torrent.AbsId);
        Assert.DoesNotContain(calledPaths, p => p.Contains("/search"));
    }

    [Fact]
    public async Task RunAsync_TorrentWithNoAuthors_NeverAttemptsMatch()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Book", authors: []);
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths, [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        var matcher = new AbsMatcher(context, absClient);

        await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Null(torrent.AbsId);
        Assert.Empty(calledPaths);
    }

    [Fact]
    public async Task RunAsync_TorrentAlreadyMatched_SkippedWithoutAnyAbsCall()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedTorrentAsync(context, "/data/audiobooks/Book", ["Brandon Sanderson"], absId: "existing-item");
        var calledPaths = new List<string>();
        var absClient = CreateAbsClient(
            calledPaths, [("f1", "/data/audiobooks")], new Dictionary<string, (string, string)>(), new Dictionary<string, (string, string)>());
        var matcher = new AbsMatcher(context, absClient);

        await matcher.RunAsync();

        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal("existing-item", torrent.AbsId);
        Assert.Empty(calledPaths);
    }
}
