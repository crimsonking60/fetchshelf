using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
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

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b", "m4a", "mp3"],
        EbookTypes = ["epub", "pdf"],
        MusicTypes = ["flac", "mp3", "pdf"],
        RadioTypes = ["mp3"],
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteSourceFile(string relativePath, string content = "data")
    {
        var fullPath = Path.Combine(DownloadsDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private LinkRunner CreateRunner(FetchshelfDbContext context, IMetadataLookup lookup, IReadOnlyList<Library> libraries) =>
        new(context, new EventStore(context), lookup, libraries, Formats, excludeNarratorInLibraryDir: false);

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
    public async Task RunAsync_MusicologyTorrentWithFlacAndPdf_LinksBothUsingMusicTypes()
    {
        WriteSourceFile("album/01.flac");
        WriteSourceFile("album/02.flac");
        WriteSourceFile("album/booklet.pdf");
        WriteSourceFile("album/cover.jpg");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = OrganizerTestSupport.BuildMetadata() with { MediaType = MediaType.Musicology, LegacyCategory = LegacyCategory.MusicologyMusicCompleteEditions };
        var runner = CreateRunner(context, new FakeMetadataLookup(metadata), [CreateLibrary(LibraryDir, category: "music")]);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir, category: "music")],
            new Dictionary<string, List<QBittorrentFile>>
            {
                ["hash1"] =
                [
                    new() { Name = "album/01.flac", Size = 4 },
                    new() { Name = "album/02.flac", Size = 4 },
                    new() { Name = "album/booklet.pdf", Size = 4 },
                    new() { Name = "album/cover.jpg", Size = 4 },
                ],
            });

        await runner.RunAsync(new QBittorrentConnection(), client);

        Assert.Empty(context.ErroredTorrents);
        var saved = await context.Torrents.SingleAsync();
        Assert.Equal("flac", saved.ChosenAudioExtension);
        Assert.Equal("pdf", saved.ChosenEbookExtension);
        Assert.True(File.Exists(Path.Combine(saved.LibraryPath!, "01.flac")));
        Assert.True(File.Exists(Path.Combine(saved.LibraryPath!, "booklet.pdf")));
        Assert.False(File.Exists(Path.Combine(saved.LibraryPath!, "cover.jpg")));
    }

    [Fact]
    public async Task RunAsync_AudiobookWithOnlyFlac_IsNotLinkedBecauseFlacIsOnlyAMusicType()
    {
        WriteSourceFile("book.flac");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), [CreateLibrary(LibraryDir)]);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir)], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.flac", Size = 4 }] });

        await runner.RunAsync(new QBittorrentConnection(), client);

        Assert.Empty(context.Torrents);
        Assert.Equal(ErrorStage.Linker, (await context.ErroredTorrents.SingleAsync()).Stage);
    }

    [Fact]
    public async Task RunAsync_SameTorrentFailsOnEveryRun_KeepsOneErrorRowNamedAfterTheTorrent()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata(authors: [])), [CreateLibrary(LibraryDir)]);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir)], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(new QBittorrentConnection(), client);
        var firstOccurredAt = (await context.ErroredTorrents.SingleAsync()).OccurredAt;
        await runner.RunAsync(new QBittorrentConnection(), client);
        await runner.RunAsync(new QBittorrentConnection(), client);

        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal("Some Torrent", error.Title);
        Assert.True(error.OccurredAt >= firstOccurredAt);
    }

    [Fact]
    public async Task RunAsync_TorrentLinksAfterAnEarlierFailure_ClearsItsError()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir)], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await CreateRunner(context, new FakeMetadataLookup(result: null), libraries).RunAsync(new QBittorrentConnection(), client);
        Assert.Single(context.ErroredTorrents);

        await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries).RunAsync(new QBittorrentConnection(), client);

        Assert.Empty(context.ErroredTorrents);
        Assert.NotNull((await context.Torrents.SingleAsync()).LibraryPath);
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

        var summary = await runner.RunAsync(connection, client);

        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal(ErrorStage.Linker, error.Stage);
        Assert.Equal("bad-hash", error.Key);
        Assert.Contains("genre category", error.ErrorText);
        var linked = await context.Torrents.SingleAsync();
        Assert.Equal("good-hash", linked.Id);
        Assert.Equal(new LinkRunSummary(Linked: 1, Failed: 1), summary);
    }

    [Fact]
    public async Task RunAsync_AlreadyLinkedTorrent_CountsAsNeitherLinkedNorFailed()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var runner = CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), [CreateLibrary(LibraryDir)]);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir)],
            new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await runner.RunAsync(new QBittorrentConnection(), client);

        var secondRun = await runner.RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(new LinkRunSummary(Linked: 0, Failed: 0), secondRun);
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

        var outcome = await refreshRunner.RefreshAsync("hash1");

        Assert.Equal(RefreshOutcome.Changed, outcome);
        var updatedEvent = await context.Events.SingleAsync(e => e.Kind == EventKind.Updated);
        Assert.Contains("Refreshed Title", updatedEvent.FieldChangesJson);
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_FolderChanged_MovesTheBookToTheNewFolder()
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
        var result = await refreshRunner.RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded);
        var updated = await context.Torrents.SingleAsync();
        Assert.NotEqual(oldPath, updated.LibraryPath);
        Assert.False(Directory.Exists(oldPath));
        Assert.True(File.Exists(Path.Combine(updated.LibraryPath!, "book.m4b")));
    }

    /// <summary>Links "hash1" (book.m4b) once and returns its qBittorrent client and library folder.</summary>
    private async Task<(QBittorrentClient Client, string LibraryPath)> LinkOnceAsync(
        FetchshelfDbContext context, IReadOnlyList<Library> libraries, string category = "audiobooks")
    {
        WriteSourceFile("book.m4b");
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir, category)],
            new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries)
            .RunAsync(new QBittorrentConnection(), client);
        return (client, (await context.Torrents.SingleAsync()).LibraryPath!);
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_FolderDeletedByHand_LinksTheBookAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (client, libraryPath) = await LinkOnceAsync(context, libraries);
        Directory.Delete(libraryPath, recursive: true);

        var result = await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries)
            .RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded, result.Message);
        Assert.StartsWith("Hardlinked Some Torrent into ", result.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(libraryPath, "book.m4b")));
        Assert.True(File.Exists(Path.Combine(libraryPath, "metadata.json")));
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_CategoryNowPointsToAnotherLibrary_MovesTheBookThere()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var otherLibraryDir = Path.Combine(_root, "other-library");
        var libraries = new List<Library> { CreateLibrary(LibraryDir), CreateLibrary(otherLibraryDir, category: "other") };
        var (_, oldPath) = await LinkOnceAsync(context, libraries);
        var recategorized = OrganizerTestSupport.CreateQBittorrentClient(
            [FinishedTorrent("hash1", DownloadsDir, category: "other")],
            new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        var result = await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries)
            .RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), recategorized)]);

        Assert.True(result.Succeeded, result.Message);
        var updated = await context.Torrents.SingleAsync();
        Assert.StartsWith(otherLibraryDir, updated.LibraryPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(updated.LibraryPath!, "book.m4b")));
        Assert.False(Directory.Exists(oldPath));
        Assert.Null(updated.LibraryMismatchState);
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_MetadataEditedByHand_LinksByTheEditedMetadata()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (client, oldPath) = await LinkOnceAsync(context, libraries);
        var runner = CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata(title: "MAM Title")), libraries);
        await runner.SetManualMetadataAsync("hash1", OrganizerTestSupport.BuildMetadata(title: "Fixed Title"));

        var result = await runner.RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded, result.Message);
        var updated = await context.Torrents.SingleAsync();
        Assert.EndsWith("Fixed Title", updated.LibraryPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(updated.LibraryPath!, "book.m4b")));
        Assert.False(Directory.Exists(oldPath));
        var stored = TorrentMetadataSerializer.Deserialize(updated.MetadataJson);
        Assert.Equal(("Fixed Title", MetadataSource.Manual), (stored.Title, stored.MetadataSource));
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_NotInQBittorrent_ChangesNothing()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (_, libraryPath) = await LinkOnceAsync(context, libraries);
        var emptyClient = OrganizerTestSupport.CreateQBittorrentClient([], new Dictionary<string, List<QBittorrentFile>>());

        var result = await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries)
            .RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), emptyClient)]);

        Assert.False(result.Succeeded);
        Assert.Equal("Not relinked: It is not in qBittorrent, so there is nothing to link from. Nothing was changed.", result.Message);
        Assert.True(File.Exists(Path.Combine(libraryPath, "book.m4b")));
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_SourceFilesMissing_LeavesTheLibraryAlone()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (client, libraryPath) = await LinkOnceAsync(context, libraries);
        File.Delete(Path.Combine(DownloadsDir, "book.m4b"));

        var result = await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries)
            .RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.False(result.Succeeded);
        Assert.Contains("1 of its files are not in", result.Message);
        Assert.True(File.Exists(Path.Combine(libraryPath, "book.m4b")));
    }

    [Fact]
    public async Task RelinkAsync_StoredTitleChanged_MovesTheBookWithoutAskingMam()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (client, oldPath) = await LinkOnceAsync(context, libraries);
        var stored = await context.Torrents.SingleAsync();
        stored.MetadataJson = TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata(title: "Stored Title"));
        await context.SaveChangesAsync();

        var result = await CreateRunner(context, new FakeMetadataLookup(null), libraries)
            .RelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded, result.Message);
        var updated = await context.Torrents.SingleAsync();
        Assert.EndsWith("Stored Title", updated.LibraryPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(updated.LibraryPath!, "book.m4b")));
        Assert.False(Directory.Exists(oldPath));
    }

    [Fact]
    public async Task RelinkAsync_ReplacedTorrent_IsLinkedAndNoLongerMarkedReplaced()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (client, libraryPath) = await LinkOnceAsync(context, libraries);
        var replaced = await context.Torrents.SingleAsync();
        LibraryFileRemover.Remove(libraryPath, [new("book.m4b", 4)], folderSharedWithAnotherTorrent: false);
        (replaced.LibraryPath, replaced.ReplacedWithTorrentId, replaced.ReplacedAt) = (null, "better", DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        var result = await CreateRunner(context, new FakeMetadataLookup(null), libraries)
            .RelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded, result.Message);
        var updated = await context.Torrents.SingleAsync();
        Assert.Equal(libraryPath, updated.LibraryPath);
        Assert.Null(updated.ReplacedWithTorrentId);
        Assert.True(File.Exists(Path.Combine(libraryPath, "book.m4b")));
    }

    [Fact]
    public async Task RunAsync_TorrentReplacedByTheCleaner_IsNotLinkedAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var (client, libraryPath) = await LinkOnceAsync(context, libraries);
        var replaced = await context.Torrents.SingleAsync();
        LibraryFileRemover.Remove(libraryPath, [new("book.m4b", 4)], folderSharedWithAnotherTorrent: false);
        (replaced.LibraryPath, replaced.LinkedFilesJson, replaced.ReplacedWithTorrentId) = (null, null, "better");
        await context.SaveChangesAsync();

        var summary = await CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries)
            .RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(new LinkRunSummary(0, 0), summary);
        var after = await context.Torrents.SingleAsync();
        Assert.Null(after.LibraryPath);
        Assert.Equal("better", after.ReplacedWithTorrentId);
        Assert.False(Directory.Exists(libraryPath));
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
