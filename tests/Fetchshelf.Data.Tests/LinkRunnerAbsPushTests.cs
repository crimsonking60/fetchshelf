using System.Net;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>Pushing metadata to a matched Audiobookshelf item on link and refresh.</summary>
public sealed class LinkRunnerAbsPushTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-link-runner-abs-tests-").FullName;

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

    private static AbsClient CreateAbsClient(List<string> patchedItemIds)
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                var itemId = request.RequestUri!.AbsolutePath.Split('/')[3];
                patchedItemIds.Add(itemId);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        return new AbsClient(new HttpClient(handler), new AbsClientOptions { BaseUrl = "http://abs.local", Token = "token" });
    }

    private LinkRunner CreateRunner(FetchshelfDbContext context, IMetadataLookup lookup, IReadOnlyList<Library> libraries, AbsClient? absClient) =>
        new(context, new EventStore(context), lookup, libraries, Formats, excludeNarratorInLibraryDir: false, absClient);

    private static Library CreateLibrary(string libraryDir, string category = "audiobooks") => new()
    {
        LibraryDir = libraryDir,
        Category = category,
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
    public async Task RunAsync_TrackedTorrentAlreadyMatched_PushesMetadataAfterLinking()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var metadata = OrganizerTestSupport.BuildMetadata(title: "Manual Entry");

        // Tracked (manual metadata) but not yet linked, with an ABS id already stored - an edge
        // case the matcher itself never produces (it requires LibraryPath already set), but still a
        // valid precondition for this hook to handle correctly.
        var noAbsLinkRunner = CreateRunner(context, new FakeMetadataLookup(), libraries, absClient: null);
        await noAbsLinkRunner.SetManualMetadataAsync("hash1", metadata);
        var tracked = await context.Torrents.SingleAsync();
        tracked.AbsId = "item1";
        await context.SaveChangesAsync();

        var patchedItemIds = new List<string>();
        var absClient = CreateAbsClient(patchedItemIds);
        var runner = CreateRunner(context, new FakeMetadataLookup(), libraries, absClient);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(["item1"], patchedItemIds);
    }

    [Fact]
    public async Task RunAsync_UntrackedNewTorrent_NoPushSinceNoAbsIdYet()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var patchedItemIds = new List<string>();
        var absClient = CreateAbsClient(patchedItemIds);
        var runner = CreateRunner(context, new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata()), libraries, absClient);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });

        await runner.RunAsync(new QBittorrentConnection(), client);

        Assert.Empty(patchedItemIds);
    }

    [Fact]
    public async Task RefreshAsync_ChangedMetadataWithStoredAbsId_PushesUpdatedMetadata()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var originalMetadata = OrganizerTestSupport.BuildMetadata(title: "Original Title");
        var linkRunner = CreateRunner(context, new FakeMetadataLookup(originalMetadata), libraries, absClient: null);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await linkRunner.RunAsync(new QBittorrentConnection(), client);
        var tracked = await context.Torrents.SingleAsync();
        tracked.AbsId = "item1";
        await context.SaveChangesAsync();

        var patchedItemIds = new List<string>();
        var absClient = CreateAbsClient(patchedItemIds);
        var refreshRunner = CreateRunner(
            context, new FakeMetadataLookup(originalMetadata with { Title = "Refreshed Title" }), libraries, absClient);

        var outcome = await refreshRunner.RefreshAsync("hash1");

        Assert.Equal(RefreshOutcome.Changed, outcome);
        Assert.Equal(["item1"], patchedItemIds);
    }

    [Fact]
    public async Task RefreshAsync_NoChanges_NoPushEvenWithStoredAbsId()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var metadata = OrganizerTestSupport.BuildMetadata(title: "Same Title");
        var linkRunner = CreateRunner(context, new FakeMetadataLookup(metadata), libraries, absClient: null);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await linkRunner.RunAsync(new QBittorrentConnection(), client);
        var tracked = await context.Torrents.SingleAsync();
        tracked.AbsId = "item1";
        await context.SaveChangesAsync();

        var patchedItemIds = new List<string>();
        var absClient = CreateAbsClient(patchedItemIds);
        var refreshRunner = CreateRunner(context, new FakeMetadataLookup(metadata), libraries, absClient);

        var outcome = await refreshRunner.RefreshAsync("hash1");

        Assert.Equal(RefreshOutcome.Unchanged, outcome);
        Assert.Empty(patchedItemIds);
    }

    [Fact]
    public async Task RefreshAsync_ChangedMetadataWithNoStoredAbsId_NoPush()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var originalMetadata = OrganizerTestSupport.BuildMetadata(title: "Original Title");
        var linkRunner = CreateRunner(context, new FakeMetadataLookup(originalMetadata), libraries, absClient: null);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await linkRunner.RunAsync(new QBittorrentConnection(), client);

        var patchedItemIds = new List<string>();
        var absClient = CreateAbsClient(patchedItemIds);
        var refreshRunner = CreateRunner(
            context, new FakeMetadataLookup(originalMetadata with { Title = "Refreshed Title" }), libraries, absClient);

        var outcome = await refreshRunner.RefreshAsync("hash1");

        Assert.Equal(RefreshOutcome.Changed, outcome);
        Assert.Empty(patchedItemIds);
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_NewFolderWithStoredAbsId_DeletesTheOldItemAndClearsTheId()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var originalMetadata = OrganizerTestSupport.BuildMetadata(title: "Original Title");
        var linkRunner = CreateRunner(context, new FakeMetadataLookup(originalMetadata), libraries, absClient: null);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await linkRunner.RunAsync(new QBittorrentConnection(), client);
        var tracked = await context.Torrents.SingleAsync();
        tracked.AbsId = "item1";
        await context.SaveChangesAsync();

        var requests = new List<string>();
        var absClient = new AbsClient(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            new AbsClientOptions { BaseUrl = "http://abs.local", Token = "token" });
        var refreshRunner = CreateRunner(
            context, new FakeMetadataLookup(originalMetadata with { Title = "Brand New Title" }), libraries, absClient);

        var result = await refreshRunner.RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded);
        Assert.Equal(["DELETE /api/items/item1"], requests);
        Assert.Null((await context.Torrents.SingleAsync()).AbsId);
    }

    [Fact]
    public async Task RefreshAndRelinkAsync_SameFolderWithStoredAbsId_PushesTheMetadata()
    {
        WriteSourceFile("book.m4b");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var libraries = new List<Library> { CreateLibrary(LibraryDir) };
        var metadata = OrganizerTestSupport.BuildMetadata(title: "Same Title");
        var linkRunner = CreateRunner(context, new FakeMetadataLookup(metadata), libraries, absClient: null);
        var torrent = FinishedTorrent("hash1", DownloadsDir);
        var client = OrganizerTestSupport.CreateQBittorrentClient(
            [torrent], new Dictionary<string, List<QBittorrentFile>> { ["hash1"] = [new() { Name = "book.m4b", Size = 4 }] });
        await linkRunner.RunAsync(new QBittorrentConnection(), client);
        var tracked = await context.Torrents.SingleAsync();
        tracked.AbsId = "item1";
        await context.SaveChangesAsync();

        var patchedItemIds = new List<string>();
        var refreshRunner = CreateRunner(context, new FakeMetadataLookup(metadata), libraries, CreateAbsClient(patchedItemIds));

        var result = await refreshRunner.RefreshAndRelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded);
        Assert.Equal(["item1"], patchedItemIds);
        Assert.Equal("item1", (await context.Torrents.SingleAsync()).AbsId);
    }
}
