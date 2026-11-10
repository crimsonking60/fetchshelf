using System.Net;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public sealed class ClientStatusCheckerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-client-status-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static QBittorrentTorrent InClient(string hash, string state, string category = "audiobooks", double progress = 1.0) => new()
    {
        Hash = hash,
        Name = "Book",
        SavePath = "/downloads",
        Category = category,
        State = state,
        Progress = progress,
    };

    private static async Task SeedAsync(FetchshelfDbContext context, string id, string? clientStatus = null, string? category = null)
    {
        var metadata = OrganizerTestSupport.BuildMetadata(title: $"Book {id}");
        context.Torrents.Add(new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
            ClientStatus = clientStatus,
            QBittorrentCategory = category,
        });
        await context.SaveChangesAsync();
    }

    private static QBittorrentClient Unreachable(string url) => new(
        new HttpClient(new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"))),
        new QBittorrentClientOptions(url, "admin", "pw"));

    private static QBittorrentClient WithTorrents(params QBittorrentTorrent[] torrents) =>
        OrganizerTestSupport.CreateQBittorrentClient(torrents, new Dictionary<string, List<QBittorrentFile>>());

    [Fact]
    public async Task CheckAsync_TrackedTorrentsInClient_GetTheirCurrentStateAndCategory()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash-downloading", clientStatus: null);
        await SeedAsync(context, "hash-seeding", clientStatus: "pausedUP", category: "old");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([WithTorrents(
            InClient("hash-downloading", "downloading", progress: 0.4), InClient("hash-seeding", "uploading", category: "ebooks"))]);

        Assert.True(result.Succeeded);
        Assert.Equal((2, 2, 0), (result.Checked, result.FoundInClient, result.NotInClient));
        Assert.Equal("downloading", context.Torrents.Single(t => t.Id == "hash-downloading").ClientStatus); // not finished: the Link job would never have set it
        var seeding = context.Torrents.Single(t => t.Id == "hash-seeding");
        Assert.Equal("uploading", seeding.ClientStatus);
        Assert.Equal("ebooks", seeding.QBittorrentCategory);
    }

    [Fact]
    public async Task CheckAsync_TorrentNoClientHas_IsMarkedNotInClient()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "gone", clientStatus: "uploading");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([WithTorrents(InClient("other", "uploading"))]);

        Assert.Equal(1, result.NotInClient);
        Assert.Equal(ClientStatuses.NotInClient, context.Torrents.Single().ClientStatus);
    }

    [Theory]
    [InlineData("gone", "not in client")]
    [InlineData("here", "uploading")]
    public async Task CheckAsync_TorrentRemovedFromMam_KeepsTheMarkWhateverQBittorrentSays(string id, string expectedStatus)
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, id, clientStatus: "stoppedUP");
        var removedAt = new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
        context.Torrents.Single().RemovedFromMamAt = removedAt;
        await context.SaveChangesAsync();
        var checker = new ClientStatusChecker(context);

        await checker.CheckAsync([WithTorrents(InClient("here", "uploading"))]);

        var torrent = context.Torrents.Single();
        Assert.Equal(expectedStatus, torrent.ClientStatus);
        Assert.Equal(removedAt, torrent.RemovedFromMamAt);
    }

    [Fact]
    public async Task CheckAsync_HashInDifferentCase_StillMatches()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "ABCDEF");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([WithTorrents(InClient("abcdef", "stalledUP"))]);

        Assert.Equal(1, result.FoundInClient);
        Assert.Equal("stalledUP", context.Torrents.Single().ClientStatus);
    }

    [Fact]
    public async Task CheckAsync_ATorrentOnEitherConnection_IsFound()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "on-b");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([WithTorrents(InClient("on-a", "uploading")), WithTorrents(InClient("on-b", "stalledUP"))]);

        Assert.Equal(1, result.FoundInClient);
        Assert.Equal("stalledUP", context.Torrents.Single().ClientStatus);
    }

    [Fact]
    public async Task CheckAsync_OneConnectionUnreachable_UpdatesWhatItFoundButNeverMarksOthersNotInClient()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "reachable");
        await SeedAsync(context, "maybe-on-the-dead-one", clientStatus: "uploading");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([WithTorrents(InClient("reachable", "stalledUP")), Unreachable("http://nas:8080")]);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.FoundInClient);
        Assert.Equal(0, result.NotInClient);
        var error = Assert.Single(result.Errors);
        Assert.Contains("http://nas:8080", error);
        Assert.Contains("Connection refused", error);
        Assert.Equal("stalledUP", context.Torrents.Single(t => t.Id == "reachable").ClientStatus);
        Assert.Equal("uploading", context.Torrents.Single(t => t.Id == "maybe-on-the-dead-one").ClientStatus);
    }

    [Fact]
    public async Task CheckAsync_Timeout_IsReportedAsNotRespondingInTime()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "x");
        var slow = new QBittorrentClient(
            new HttpClient(new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timeout", new TimeoutException()))),
            new QBittorrentClientOptions("http://slow:8080", "admin", "pw"));
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([slow]);

        Assert.Contains("did not respond in time", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task CheckAsync_NoConnectionConfigured_ChangesNothingAndSaysWhy()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "x", clientStatus: "uploading");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([]);

        Assert.Contains("No qBittorrent connection", Assert.Single(result.Errors));
        Assert.Equal("uploading", context.Torrents.Single().ClientStatus);
    }

    [Fact]
    public async Task CheckAsync_MetadataOnlyRecord_IsSkippedAndLosesAnEarlierNotInClientStatus()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "hash1");
        await SeedAsync(context, "mam:42", clientStatus: ClientStatuses.NotInClient);
        await SeedAsync(context, "mam:43");
        var checker = new ClientStatusChecker(context);

        var result = await checker.CheckAsync([WithTorrents(InClient("hash1", "uploading"))]);

        Assert.Equal((1, 1, 0, 2), (result.Checked, result.FoundInClient, result.NotInClient, result.MetadataOnlySkipped));
        Assert.All(context.Torrents.Where(t => t.Id.StartsWith("mam:")), t => Assert.Null(t.ClientStatus));
    }
}
