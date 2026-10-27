using System.Net;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>Covers task 5.1: deleting a replaced torrent's matched Audiobookshelf item.</summary>
public sealed class CleanerAbsDeleteTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-cleaner-abs-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    private static readonly IReadOnlyList<string> AudioTypes = ["m4b", "m4a", "mp3"];
    private static readonly IReadOnlyList<string> EbookTypes = ["epub", "pdf"];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Cleaner CreateCleaner(FetchshelfDbContext context, AbsClient? absClient) =>
        new(context, new EventStore(context), AudioTypes, EbookTypes, absClient);

    private static AbsClient CreateAbsClient(List<string> deletedItemIds)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                deletedItemIds.Add(request.RequestUri!.AbsolutePath.Split('/')[3]);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        return new AbsClient(new HttpClient(handler), new AbsClientOptions { BaseUrl = "http://abs.local", Token = "token" });
    }

    private async Task<TorrentEntity> AddLinkedTorrentAsync(
        FetchshelfDbContext context, string id, string audioExtension, long sizeBytes, string libraryPath, string? absId = null)
    {
        Directory.CreateDirectory(libraryPath);
        var fileName = $"book.{audioExtension}";
        File.WriteAllText(Path.Combine(libraryPath, fileName), new string('x', (int)sizeBytes));

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
            AbsId = absId,
        };
        context.Torrents.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    [Fact]
    public async Task RunAsync_AutomaticReplacementWithDifferentKeeperPath_DeletesAbsItem()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var keeperPath = Path.Combine(_root, "keeper");
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, keeperPath);
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath, absId: "item1");

        var deletedItemIds = new List<string>();
        var cleaner = CreateCleaner(context, CreateAbsClient(deletedItemIds));
        await cleaner.RunAsync([]);

        Assert.Equal(["item1"], deletedItemIds);
        var loser = await context.Torrents.SingleAsync(t => t.Id == "loser");
        Assert.Null(loser.AbsId);
    }

    [Fact]
    public async Task RunAsync_AutomaticReplacementWhenKeeperSharesMemberPath_DoesNotDeleteAbsItem()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var sharedPath = Path.Combine(_root, "shared");
        Directory.CreateDirectory(sharedPath);
        // Both members happen to share the same library path (an edge case the keeper-path check
        // exists for - see Cleaner.RunAsync).
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, sharedPath);
        await AddLinkedTorrentAsync(context, "loser", "mp3", 50, sharedPath, absId: "item1");

        var deletedItemIds = new List<string>();
        var cleaner = CreateCleaner(context, CreateAbsClient(deletedItemIds));
        await cleaner.RunAsync([]);

        Assert.Empty(deletedItemIds);
    }

    [Fact]
    public async Task CleanOneAsync_ManualClean_AlwaysDeletesAbsItemRegardlessOfPath()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var sharedPath = Path.Combine(_root, "shared");
        Directory.CreateDirectory(sharedPath);
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, sharedPath);
        await AddLinkedTorrentAsync(context, "loser", "mp3", 50, sharedPath, absId: "item1");

        var deletedItemIds = new List<string>();
        var cleaner = CreateCleaner(context, CreateAbsClient(deletedItemIds));
        var cleaned = await cleaner.CleanOneAsync("loser", []);

        Assert.True(cleaned);
        Assert.Equal(["item1"], deletedItemIds);
    }

    [Fact]
    public async Task CleanOneAsync_NoConfiguredAbsClient_NoAbsCallEvenWithStoredId()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var keeperPath = Path.Combine(_root, "keeper");
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, keeperPath);
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath, absId: "item1");

        var cleaner = CreateCleaner(context, absClient: null);
        var cleaned = await cleaner.CleanOneAsync("loser", []);

        Assert.True(cleaned);
        var loser = await context.Torrents.SingleAsync(t => t.Id == "loser");
        Assert.Null(loser.AbsId);
    }

    [Fact]
    public async Task CleanOneAsync_MemberHasNoStoredAbsId_NoAbsCall()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var keeperPath = Path.Combine(_root, "keeper");
        var loserPath = Path.Combine(_root, "loser");
        await AddLinkedTorrentAsync(context, "keeper", "m4b", 100, keeperPath);
        await AddLinkedTorrentAsync(context, "loser", "mp3", 100, loserPath, absId: null);

        var deletedItemIds = new List<string>();
        var cleaner = CreateCleaner(context, CreateAbsClient(deletedItemIds));
        await cleaner.CleanOneAsync("loser", []);

        Assert.Empty(deletedItemIds);
    }
}
