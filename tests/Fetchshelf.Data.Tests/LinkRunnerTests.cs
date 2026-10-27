using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class LinkRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-link-runner-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");
    private string DownloadsDir => Path.Combine(_root, "downloads");
    private string LibraryDir => Path.Combine(_root, "library");

    private static readonly IReadOnlyList<string> AudioTypes = ["m4b", "m4a", "mp3"];
    private static readonly IReadOnlyList<string> EbookTypes = ["epub", "pdf"];

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteSourceFile(string relativePath, string content = "data")
    {
        var fullPath = Path.Combine(DownloadsDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private LinkRunner CreateRunner(FetchshelfDbContext context, IMetadataLookup lookup, IReadOnlyList<Library> libraries) =>
        new(context, new EventStore(context), lookup, libraries, AudioTypes, EbookTypes, excludeNarratorInLibraryDir: false);

    private static Library CreateLibrary(string libraryDir, string category = "audiobooks") => new()
    {
        LibraryDir = libraryDir,
        Category = category,
        Method = LibraryLinkMethod.Hardlink,
    };

    private static Library CreateLibraryByDownloadDir(string libraryDir, string downloadDir) => new()
    {
        LibraryDir = libraryDir,
        DownloadDir = downloadDir,
        Method = LibraryLinkMethod.Hardlink,
    };

    private static QBittorrentTorrent FinishedTorrent(string hash, string savePath, string category = "audiobooks") => new()
    {
        Hash = hash,
        Name = "Some Torrent",
        SavePath = savePath,
        Category = category,
        State = "uploading",
        Progress = 1.0,
    };

    [Fact]
    public async Task RunAsync_UntrackedTorrentWithMetadataFound_LinksAndCreatesTorrentAndEvent()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata());
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var runner = CreateRunner(context, lookup, libraries);
        var connection = new QBittorrentConnection();
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(connection, client);

        var saved = await context.Torrents.SingleAsync();
        Assert.Equal("hash1", saved.Id);
        Assert.NotNull(saved.LibraryPath);
        Assert.True(File.Exists(Path.Combine(saved.LibraryPath!, "book.m4b")));
        Assert.True(File.Exists(Path.Combine(saved.LibraryPath!, "metadata.json")));
        var events = await context.Events.ToListAsync();
        Assert.Single(events);
        Assert.Equal(EventKind.Linked, events[0].Kind);
    }

    [Fact]
    public async Task RunAsync_AuthorlessMetadata_RecordsLinkerErrorAndDoesNotLink()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata(authors: []));
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var runner = CreateRunner(context, lookup, libraries);
        var connection = new QBittorrentConnection();
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(connection, client);

        Assert.Empty(context.Torrents);
        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal(ErrorStage.Linker, error.Stage);
        Assert.Equal("hash1", error.Key);
    }

    [Fact]
    public async Task RunAsync_LookupThrowsForOneTorrent_RecordsErrorAndStillLinksTheRest()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new ThrowingForHashLookup("bad-hash", OrganizerTestSupport.BuildMetadata());
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var runner = CreateRunner(context, lookup, libraries);
        var connection = new QBittorrentConnection();
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("bad-hash", DownloadsDir), FinishedTorrent("good-hash", DownloadsDir)],
            new Dictionary<string, List<QBittorrentFile>>
            {
                ["bad-hash"] = [new() { Name = "book.m4b", Size = 4 }],
                ["good-hash"] = [new() { Name = "book.m4b", Size = 4 }],
            });

        await runner.RunAsync(connection, client);

        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal(ErrorStage.Linker, error.Stage);
        Assert.Equal("bad-hash", error.Key);
        Assert.Contains("genre category", error.ErrorText);
        var linked = await context.Torrents.SingleAsync();
        Assert.Equal("good-hash", linked.Id);
    }

    private sealed class ThrowingForHashLookup(string badHash, TorrentMetadata result) : IMetadataLookup
    {
        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
            infoHash == badHash
                ? throw new UnrecognizedReferenceDataException("genre category", 63)
                : Task.FromResult<TorrentMetadata?>(result);
    }

    [Fact]
    public async Task RunAsync_UntrackedTorrentMatchingNoLibrary_IsIgnoredWithoutMetadataLookup()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup();
        var libraries = new List<Library> { CreateLibrary(LibraryDir, category: "audiobooks") };
        var runner = CreateRunner(context, lookup, libraries);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("other-hash", DownloadsDir, category: "movies")], new Dictionary<string, List<QBittorrentFile>>());

        await runner.RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(0, lookup.Calls);
        Assert.Empty(context.Torrents);
        Assert.Empty(context.ErroredTorrents);
    }

    private sealed class CountingLookup : IMetadataLookup
    {
        public int Calls { get; private set; }

        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<TorrentMetadata?>(null);
        }
    }

    [Fact]
    public async Task RunAsync_MetadataNotFound_RecordsLinkerError()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new FakeMetadataLookup(result: null);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var runner = CreateRunner(context, lookup, libraries);
        var connection = new QBittorrentConnection();
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(connection, client);

        Assert.Empty(context.Torrents);
        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal(ErrorStage.Linker, error.Stage);
    }

    [Fact]
    public async Task RunAsync_AlreadyLinkedTorrent_SyncsCategoryAndFlagsMismatchOnMetadataChange()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = OrganizerTestSupport.BuildMetadata(title: "Old Title");
        // Matched by download_dir rather than category, so changing the reported category afterward
        // does not itself affect library matching - isolating the "category synced" assertion from
        // the "folder-affecting metadata change is flagged" assertion.
        var libraries = new List<Library> { CreateLibraryByDownloadDir(LibraryDir, DownloadsDir) };

        // Link once.
        var firstLookup = new FakeMetadataLookup(metadata);
        var firstRunner = CreateRunner(context, firstLookup, libraries);
        var torrent = FinishedTorrent("hash1", DownloadsDir, category: "audiobooks");
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await firstRunner.RunAsync(new QBittorrentConnection(), client);

        // Change the stored metadata (simulating a prior refresh) so the expected folder now differs,
        // then run again with an updated qBittorrent category to confirm sync + mismatch detection.
        var tracked = await context.Torrents.SingleAsync();
        tracked.MetadataJson = TorrentMetadataSerializer.Serialize(metadata with { Title = "New Title" });
        await context.SaveChangesAsync();

        var secondRunner = CreateRunner(context, new FakeMetadataLookup(), libraries);
        var changedCategoryTorrent = FinishedTorrent("hash1", DownloadsDir, category: "new-category");
        var secondClient = OrganizerTestSupport.CreateQBittorrentClient(
            [changedCategoryTorrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await secondRunner.RunAsync(new QBittorrentConnection(), secondClient);

        var updated = await context.Torrents.SingleAsync();
        Assert.Equal("new-category", updated.QBittorrentCategory);
        Assert.Equal(nameof(LibraryMismatchState.NewPath), updated.LibraryMismatchState);
    }

    [Fact]
    public async Task RefreshAsync_FieldChanged_RecordsUpdatedEventWithDiff()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var originalMetadata = OrganizerTestSupport.BuildMetadata(title: "Original Title");
        var runner = CreateRunner(context, new FakeMetadataLookup(originalMetadata), libraries);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await runner.RunAsync(new QBittorrentConnection(), client);

        var refreshLookup = new FakeMetadataLookup(originalMetadata with { Title = "Refreshed Title" });
        var refreshRunner = CreateRunner(context, refreshLookup, libraries);

        var changed = await refreshRunner.RefreshAsync("hash1", relink: false);

        Assert.True(changed);
        var updatedEvent = await context.Events.SingleAsync(e => e.Kind == EventKind.Updated);
        Assert.Contains("Refreshed Title", updatedEvent.FieldChangesJson);
    }

    [Fact]
    public async Task RefreshAsync_WithRelinkAndFolderChanged_MovesFiles()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var originalMetadata = OrganizerTestSupport.BuildMetadata(title: "Original Title");
        var runner = CreateRunner(context, new FakeMetadataLookup(originalMetadata), libraries);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await runner.RunAsync(new QBittorrentConnection(), client);
        var oldPath = (await context.Torrents.SingleAsync()).LibraryPath!;

        var refreshLookup = new FakeMetadataLookup(originalMetadata with { Title = "Brand New Title" });
        var refreshRunner = CreateRunner(context, refreshLookup, libraries);
        await refreshRunner.RefreshAsync("hash1", relink: true);

        var updated = await context.Torrents.SingleAsync();
        Assert.NotEqual(oldPath, updated.LibraryPath);
        Assert.False(Directory.Exists(oldPath));
        Assert.True(File.Exists(Path.Combine(updated.LibraryPath!, "book.m4b")));
    }

    [Fact]
    public async Task RunAsync_ManualSourceTorrent_MetadataUnchangedByPlainLinkRun()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var runner = CreateRunner(context, new FakeMetadataLookup(), libraries);
        var manualMetadata = OrganizerTestSupport.BuildMetadata(title: "Manual Title") with { MetadataSource = MetadataSource.Manual };
        await runner.SetManualMetadataAsync("hash1", manualMetadata);

        WriteSourceFile("book.m4b");
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        // First run: links using the manual metadata (was tracked but not yet linked).
        await runner.RunAsync(new QBittorrentConnection(), client);
        var afterFirstRun = TorrentMetadataSerializer.Deserialize((await context.Torrents.SingleAsync()).MetadataJson);
        Assert.Equal(MetadataSource.Manual, afterFirstRun.MetadataSource);

        // Second run (already linked): must not overwrite the manual metadata.
        await runner.RunAsync(new QBittorrentConnection(), client);
        var afterSecondRun = TorrentMetadataSerializer.Deserialize((await context.Torrents.SingleAsync()).MetadataJson);
        Assert.Equal("Manual Title", afterSecondRun.Title);
        Assert.Equal(MetadataSource.Manual, afterSecondRun.MetadataSource);
    }

    [Fact]
    public async Task SetManualMetadataAsync_NewTorrent_CreatesRecordWithManualSource()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context, new FakeMetadataLookup(), [CreateLibrary(LibraryDir)]);

        await runner.SetManualMetadataAsync("hash1", OrganizerTestSupport.BuildMetadata(title: "Manual Entry"));

        var saved = await context.Torrents.SingleAsync();
        var metadata = TorrentMetadataSerializer.Deserialize(saved.MetadataJson);
        Assert.Equal(MetadataSource.Manual, metadata.MetadataSource);
        Assert.Equal("Manual Entry", saved.Title);
    }
}
