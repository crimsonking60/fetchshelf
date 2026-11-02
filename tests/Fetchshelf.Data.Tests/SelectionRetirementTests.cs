using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>A started selection leaves the Selected queue once its torrent has finished downloading.</summary>
public sealed class SelectionRetirementTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-retirement-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static QBittorrentTorrent InClient(string hash, double progress, string state) => new()
    {
        Hash = hash,
        Name = "Book",
        SavePath = "/downloads",
        Category = "audiobooks",
        State = state,
        Progress = progress,
    };

    private static LinkRunner LinkRunnerFor(FetchshelfDbContext context) => new(
        context, new EventStore(context), new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), [],
        new FormatPreferenceLists { AudioTypes = ["m4b"], EbookTypes = ["epub"], MusicTypes = [], RadioTypes = [] },
        excludeNarratorInLibraryDir: false);

    private static QBittorrentClient ClientWith(params QBittorrentTorrent[] torrents) =>
        OrganizerTestSupport.CreateQBittorrentClient(torrents, new Dictionary<string, List<QBittorrentFile>>());

    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30);

    [Fact]
    public async Task LinkRun_FinishedTorrent_RetiresItsStartedSelection()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        var selection = await OrganizerTestSupport.SeedSelectionAsync(context, 1, StartedAt);

        await LinkRunnerFor(context).RunAsync(new QBittorrentConnection(), ClientWith(InClient("hash1", 1.0, "uploading")));

        var saved = await context.SelectedTorrents.AsNoTracking().SingleAsync(s => s.Id == selection.Id);
        Assert.NotNull(saved.RemovedAt);
    }

    [Fact]
    public async Task LinkRun_TorrentStillDownloading_KeepsItsSelection()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        await OrganizerTestSupport.SeedSelectionAsync(context, 1, StartedAt);

        await LinkRunnerFor(context).RunAsync(new QBittorrentConnection(), ClientWith(InClient("hash1", 0.4, "downloading")));

        Assert.Null((await context.SelectedTorrents.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task RunAsync_SomeTorrentsFinished_RetiresOnlyTheirSelections()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "done", mamId: 1);
        await OrganizerTestSupport.SeedTorrentAsync(context, "busy", mamId: 2);
        var finished = await OrganizerTestSupport.SeedSelectionAsync(context, 1, StartedAt);
        var downloading = await OrganizerTestSupport.SeedSelectionAsync(context, 2, StartedAt);
        var notStartedYet = await OrganizerTestSupport.SeedSelectionAsync(context, 3, startedAt: null);

        await LinkRunnerFor(context).RunAsync(
            new QBittorrentConnection(), ClientWith(InClient("done", 1.0, "stalledUP"), InClient("busy", 0.2, "downloading")));

        var removedAtById = await context.SelectedTorrents.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.RemovedAt);
        Assert.NotNull(removedAtById[finished.Id]);
        Assert.Null(removedAtById[downloading.Id]);
        Assert.Null(removedAtById[notStartedYet.Id]);
    }

    [Fact]
    public async Task LinkRun_NeverStartedSelectionOfAFinishedTorrent_IsLeftAlone()
    {
        // Not started means still waiting in the queue; only a started selection can have finished.
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        await OrganizerTestSupport.SeedSelectionAsync(context, 1, startedAt: null);

        await LinkRunnerFor(context).RunAsync(new QBittorrentConnection(), ClientWith(InClient("hash1", 1.0, "uploading")));

        Assert.Null((await context.SelectedTorrents.AsNoTracking().SingleAsync()).RemovedAt);
    }

    [Fact]
    public async Task LinkRun_AlreadyRetiredSelection_KeepsItsOriginalRemovalTime()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        var removedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var selection = await OrganizerTestSupport.SeedSelectionAsync(context, 1, StartedAt, removedAt);

        await LinkRunnerFor(context).RunAsync(new QBittorrentConnection(), ClientWith(InClient("hash1", 1.0, "uploading")));

        var saved = await context.SelectedTorrents.AsNoTracking().SingleAsync(s => s.Id == selection.Id);
        Assert.Equal(removedAt.ToUnixTimeSeconds(), saved.RemovedAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task StatusCheck_FinishedTorrent_RetiresItsSelectionAndReportsIt()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash2", mamId: 2);
        await OrganizerTestSupport.SeedSelectionAsync(context, 1, StartedAt);
        await OrganizerTestSupport.SeedSelectionAsync(context, 2, StartedAt);

        var result = await new ClientStatusChecker(context).CheckAsync(
            [ClientWith(InClient("hash1", 1.0, "uploading"), InClient("hash2", 0.5, "downloading"))]);

        Assert.Equal(1, result.RetiredSelections);
        var byMamId = await context.SelectedTorrents.AsNoTracking().ToDictionaryAsync(s => s.MamId, s => s.RemovedAt);
        Assert.NotNull(byMamId[1]);
        Assert.Null(byMamId[2]);
    }

    [Fact]
    public async Task StatusCheck_TorrentGoneFromTheClient_DoesNotRetireItsSelection()
    {
        // "Gone" is not "finished": the selection is only retired when the torrent is seen complete.
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", mamId: 1);
        await OrganizerTestSupport.SeedSelectionAsync(context, 1, StartedAt);

        var result = await new ClientStatusChecker(context).CheckAsync([ClientWith()]);

        Assert.Equal(0, result.RetiredSelections);
        Assert.Null((await context.SelectedTorrents.AsNoTracking().SingleAsync()).RemovedAt);
    }
}
