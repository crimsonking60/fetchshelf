using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.Tests;

public sealed class CleanerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-cleaner-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b", "m4a", "mp3"],
        EbookTypes = ["epub", "pdf"],
        MusicTypes = ["flac", "mp3", "pdf"],
        RadioTypes = ["mp3"],
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Cleaner CreateCleaner(FetchshelfDbContext context) =>
        new(context, new EventStore(context), Formats);

    private async Task<TorrentEntity> AddLinkedTorrentAsync(
        FetchshelfDbContext context, string id, string audioExtension, long sizeBytes, string libraryPath, string? title = null)
    {
        Directory.CreateDirectory(libraryPath);
        var fileName = $"{id}.{audioExtension}";
        File.WriteAllText(Path.Combine(libraryPath, fileName), new string('x', (int)sizeBytes));
        File.WriteAllText(Path.Combine(libraryPath, "metadata.json"), "{}");

        var metadata = title is null ? OrganizerTestSupport.BuildMetadata() : OrganizerTestSupport.BuildMetadata(title);
        var entity = new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
            LibraryPath = libraryPath,
            LinkedFilesJson = System.Text.Json.JsonSerializer.Serialize(new List<LinkedFileRecord> { new(fileName, sizeBytes) }),
            ChosenAudioExtension = audioExtension,
        };
        context.Torrents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    [Fact]
    public async Task RunAsync_LowerRankedDuplicate_IsReplaced()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var keeperPath = Path.Combine(_root, "keeper");
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, keeperPath); // rank 0 (best)
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath); // rank 2 (worse)

        var cleaner = CreateCleaner(context);
        await cleaner.RunAsync([]);

        var loser = await context.Torrents.SingleAsync(t => t.Id == "loser");
        Assert.Equal("keeper", loser.ReplacedWithTorrentId);
        Assert.NotNull(loser.ReplacedAt);
        Assert.Null(loser.LibraryPath);
        Assert.False(File.Exists(Path.Combine(loserPath, "loser.mp3")));
        Assert.False(Directory.Exists(loserPath));

        var keeper = await context.Torrents.SingleAsync(t => t.Id == "keeper");
        Assert.Null(keeper.ReplacedWithTorrentId);

        var cleanedEvent = await context.Events.SingleAsync(e => e.Kind == EventKind.Cleaned);
        Assert.Equal("loser", cleanedEvent.TorrentId);
    }

    [Fact]
    public async Task RunAsync_DifferentBooksBySameAuthor_RetiresNeither()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var firstPath = Path.Combine(_root, "first");
        var secondPath = Path.Combine(_root, "second");
        await AddLinkedTorrentAsync(context, "first", "m4b", 100, firstPath, title: "The Way of Kings");
        await AddLinkedTorrentAsync(context, "second", "mp3", 100, secondPath, title: "Words of Radiance");

        var summary = await CreateCleaner(context).RunAsync([]);

        Assert.Equal(new CleanRunSummary(0, 0), summary);
        Assert.All(await context.Torrents.ToListAsync(), t => Assert.Null(t.ReplacedWithTorrentId));
        Assert.True(File.Exists(Path.Combine(secondPath, "second.mp3")));
    }

    [Fact]
    public async Task CleanOneAsync_DifferentBookBySameAuthor_LeavesBothLinked()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var firstPath = Path.Combine(_root, "first");
        var secondPath = Path.Combine(_root, "second");
        await AddLinkedTorrentAsync(context, "first", "m4b", 100, firstPath, title: "The Way of Kings");
        await AddLinkedTorrentAsync(context, "second", "mp3", 100, secondPath, title: "Words of Radiance");

        var cleaned = await CreateCleaner(context).CleanOneAsync("second", []);

        Assert.False(cleaned);
        Assert.True(File.Exists(Path.Combine(secondPath, "second.mp3")));
    }

    [Fact]
    public async Task RunAsync_LowerRankedDuplicate_LogsWhatWasDone()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, Path.Combine(_root, "keeper"));
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath);
        var logger = new ListLogger<Cleaner>();

        var summary = await new Cleaner(context, new EventStore(context), Formats, logger: logger).RunAsync([]);

        Assert.Equal(new CleanRunSummary(Retired: 1, Failed: 0), summary);
        var message = Assert.Single(logger.Messages(LogLevel.Information));
        Assert.StartsWith("Retired ", message, StringComparison.Ordinal);
        Assert.Contains("mp3", message);
        Assert.EndsWith($"removed 1 file from {loserPath}", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoDuplicates_LogsThatThereIsNothingToDo()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await AddLinkedTorrentAsync(context, "only", "m4b", 100, Path.Combine(_root, "only"));
        var logger = new ListLogger<Cleaner>();

        var summary = await new Cleaner(context, new EventStore(context), Formats, logger: logger).RunAsync([]);

        Assert.Equal(new CleanRunSummary(Retired: 0, Failed: 0), summary);
        Assert.Equal(["No duplicate copies to clean up"], logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_EqualRank_TieBrokenByLargerLinkedSize()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var biggerPath = Path.Combine(_root, "bigger");
        var smallerPath = Path.Combine(_root, "smaller");
        await AddLinkedTorrentAsync(context, "bigger", "m4b", 200, biggerPath);
        await AddLinkedTorrentAsync(context, "smaller", "m4b", 50, smallerPath);

        var cleaner = CreateCleaner(context);
        await cleaner.RunAsync([]);

        var smaller = await context.Torrents.SingleAsync(t => t.Id == "smaller");
        Assert.Equal("bigger", smaller.ReplacedWithTorrentId);
    }

    [Fact]
    public async Task RunAsync_ReplacedTorrent_RemainsVisibleAndRelinkable()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var keeperPath = Path.Combine(_root, "keeper");
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, keeperPath);
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath);

        var cleaner = CreateCleaner(context);
        await cleaner.RunAsync([]);

        var loser = await context.Torrents.SingleAsync(t => t.Id == "loser");
        Assert.NotNull(loser); // still present/visible, just marked replaced
        Assert.NotNull(loser.ReplacedWithTorrentId); // relinkable: no hard delete, record remains fully intact otherwise
    }

    [Fact]
    public async Task RunAsync_DuplicateInTheKeepersFolder_KeepsTheKeepersFilesAndMetadata()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var bookPath = Path.Combine(_root, "book");
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, bookPath);
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, bookPath);

        await CreateCleaner(context).RunAsync([]);

        Assert.Equal("keeper", (await context.Torrents.SingleAsync(t => t.Id == "loser")).ReplacedWithTorrentId);
        Assert.False(File.Exists(Path.Combine(bookPath, "loser.mp3")));
        Assert.True(File.Exists(Path.Combine(bookPath, "keeper.m4b")));
        Assert.True(File.Exists(Path.Combine(bookPath, "metadata.json")));
    }

    [Fact]
    public async Task RunAsync_ReplacedCopyLinkedAgain_IsRetiredAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, Path.Combine(_root, "keeper"));
        var loser = await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath);
        (loser.ReplacedWithTorrentId, loser.ReplacedAt) = ("keeper", DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var summary = await CreateCleaner(context).RunAsync([]);

        Assert.Equal(new CleanRunSummary(Retired: 1, Failed: 0), summary);
        var retired = await context.Torrents.SingleAsync(t => t.Id == "loser");
        Assert.Equal("keeper", retired.ReplacedWithTorrentId);
        Assert.Null(retired.LibraryPath);
        Assert.False(Directory.Exists(loserPath));
    }

    [Fact]
    public async Task UnlinkAsync_LinkedTorrent_RemovesItsFilesAndKeepsTheRecordMarkedUnlinked()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var bookPath = Path.Combine(_root, "book");
        await AddLinkedTorrentAsync(context, "hash1", "m4b", 100, bookPath);

        var result = await CreateCleaner(context).UnlinkAsync("hash1", []);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal($"Unlinked: removed 1 file from {bookPath}.", result.Message);
        Assert.False(Directory.Exists(bookPath));
        var unlinked = await context.Torrents.SingleAsync();
        Assert.NotNull(unlinked.UnlinkedAt);
        Assert.Null(unlinked.LibraryPath);
        Assert.Null(unlinked.ReplacedWithTorrentId);
        var unlinkedEvent = await context.Events.SingleAsync();
        Assert.Equal((EventKind.Unlinked, bookPath), (unlinkedEvent.Kind, unlinkedEvent.LibraryPath));
    }

    [Fact]
    public async Task UnlinkAsync_OnUnlinkedConfigured_SetsItsCategoryAndTagsInQBittorrent()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await AddLinkedTorrentAsync(context, "hash1", "m4b", 100, Path.Combine(_root, "book"));
        var bodies = new List<string>();
        var client = new QBittorrentClient(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("torrents/info"))
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = System.Net.Http.Json.JsonContent.Create(new[] { new QBittorrentTorrent { Hash = "hash1", Name = "Book", SavePath = "/downloads", State = "uploading" } }),
                    };
                }

                if (path.Contains("setCategory") || path.Contains("addTags"))
                {
                    bodies.Add($"{path.Split('/')[^1]}: {Uri.UnescapeDataString(request.Content!.ReadAsStringAsync().Result)}");
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") };
            })),
            new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin"));
        var connection = new QBittorrentConnection { OnUnlinked = new QBittorrentCategoryAndTags { Category = "unlinked", Tags = ["not-wanted"] } };

        var result = await CreateCleaner(context).UnlinkAsync("hash1", [(connection, client)]);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("applied on_unlinked in qBittorrent", result.Message);
        Assert.Equal(["setCategory: hashes=hash1&category=unlinked", "addTags: hashes=hash1&tags=not-wanted"], bodies);
    }

    [Fact]
    public async Task UnlinkAsync_AlreadyUnlinked_ChangesNothing()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await AddLinkedTorrentAsync(context, "hash1", "m4b", 100, Path.Combine(_root, "book"));
        var cleaner = CreateCleaner(context);
        await cleaner.UnlinkAsync("hash1", []);

        var again = await cleaner.UnlinkAsync("hash1", []);

        Assert.False(again.Succeeded);
        Assert.Single(await context.Events.ToListAsync());
    }

    [Fact]
    public async Task UnlinkAsync_MetadataOnlyRecord_IsRefused()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "mam:42", 42);

        var result = await CreateCleaner(context).UnlinkAsync("mam:42", []);

        Assert.False(result.Succeeded);
        Assert.Null((await context.Torrents.SingleAsync()).UnlinkedAt);
    }
}
