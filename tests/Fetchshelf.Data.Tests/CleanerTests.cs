using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

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
        FetchshelfDbContext context, string id, string audioExtension, long sizeBytes, string libraryPath)
    {
        Directory.CreateDirectory(libraryPath);
        var fileName = $"book.{audioExtension}";
        File.WriteAllText(Path.Combine(libraryPath, fileName), new string('x', (int)sizeBytes));
        File.WriteAllText(Path.Combine(libraryPath, "metadata.json"), "{}");

        var metadata = OrganizerTestSupport.BuildMetadata();
        var entity = new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = "the way of kings",
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
        Assert.False(File.Exists(Path.Combine(loserPath, "book.mp3")));
        Assert.False(Directory.Exists(loserPath));

        var keeper = await context.Torrents.SingleAsync(t => t.Id == "keeper");
        Assert.Null(keeper.ReplacedWithTorrentId);

        var cleanedEvent = await context.Events.SingleAsync(e => e.Kind == EventKind.Cleaned);
        Assert.Equal("loser", cleanedEvent.TorrentId);
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
}
