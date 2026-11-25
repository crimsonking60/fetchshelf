using System.Text.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>The Link job in a library with <c>layout = "series"</c>: one folder per series, one renamed file per book.</summary>
public sealed class SeriesLayoutLinkTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-series-layout-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");
    private string DownloadsDir => Path.Combine(_root, "downloads");
    private string LibraryDir => Path.Combine(_root, "comics");
    private string SagaFolder => Path.Combine(LibraryDir, "Saga");

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b", "mp3"],
        EbookTypes = ["cbz", "cbr", "epub", "pdf"],
        MusicTypes = ["flac"],
        RadioTypes = ["mp3"],
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class ByHashLookup(Dictionary<string, TorrentMetadata> metadata) : IMetadataLookup
    {
        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(metadata.GetValueOrDefault(infoHash));
    }

    private static TorrentMetadata Comic(string title, int? number = null, string series = "Saga", IReadOnlyList<string>? authors = null) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.ComicBook,
        LegacyCategory = LegacyCategory.EbookComicsGraphicNovels,
        Language = Language.English,
        Title = title,
        Authors = authors ?? ["Brian K. Vaughan"],
        Series = number is { } n
            ? [new Series { Name = series, Entries = [SeriesEntry.ForNumber(n, n.ToString(System.Globalization.CultureInfo.InvariantCulture))] }]
            : [],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    private Library SeriesLibrary() => new()
    {
        LibraryDir = LibraryDir,
        Category = "comics",
        Method = LibraryLinkMethod.Hardlink,
        Layout = LibraryLayout.Series,
        MetadataJson = false,
    };

    private Library BookLibrary() => new() { LibraryDir = LibraryDir, Category = "comics", Method = LibraryLinkMethod.Hardlink };

    /// <summary>A finished torrent in a folder of its own under downloads, with its files written there.</summary>
    private (QBittorrentTorrent Torrent, List<QBittorrentFile> Files) Torrent(string hash, params (string Name, string Content)[] files)
    {
        var savePath = Path.Combine(DownloadsDir, hash);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(savePath, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        var torrent = new QBittorrentTorrent
        {
            Hash = hash,
            Name = $"Torrent {hash}",
            SavePath = savePath,
            Category = "comics",
            State = "uploading",
            Progress = 1.0,
        };
        return (torrent, files.Select(f => new QBittorrentFile { Name = f.Name, Size = f.Content.Length }).ToList());
    }

    private static QBittorrentClient Client(params (QBittorrentTorrent Torrent, List<QBittorrentFile> Files)[] torrents) =>
        OrganizerTestSupport.CreateQBittorrentClient(
            torrents.Select(t => t.Torrent).ToList(), torrents.ToDictionary(t => t.Torrent.Hash, t => t.Files));

    private static LinkRunner Runner(FetchshelfDbContext context, Dictionary<string, TorrentMetadata> metadata, Library library) =>
        new(context, new EventStore(context), new ByHashLookup(metadata), [library], Formats, new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false));

    private static List<LinkedFileRecord> LinkedFiles(TorrentEntity torrent) =>
        JsonSerializer.Deserialize<List<LinkedFileRecord>>(torrent.LinkedFilesJson!)!;

    private static string[] FileNames(string folder) =>
        Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    private async Task<(FetchshelfDbContext Context, QBittorrentClient Client)> LinkThreeVolumesAsync()
    {
        var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(
            Torrent("hash1", ("Saga v01 (2012) (Digital).cbz", "volume one")),
            Torrent("hash2", ("Saga v02 (2013) (Digital).cbz", "volume two!")),
            Torrent("hash3", ("saga_003.cbz", "volume three")));
        var metadata = new Dictionary<string, TorrentMetadata>
        {
            ["hash1"] = Comic("Saga, Volume 1", 1),
            ["hash2"] = Comic("Saga, Volume 2", 2),
            ["hash3"] = Comic("Saga, Volume 3", 3),
        };
        await Runner(context, metadata, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);
        return (context, client);
    }

    [Fact]
    public async Task RunAsync_ThreeVolumes_LandAsRenamedFilesInOneSeriesFolder_WithOneSeriesJson()
    {
        var (context, _) = await LinkThreeVolumesAsync();
        using var _ = context;

        Assert.Equal(
            ["Saga #001 - Saga, Volume 1.cbz", "Saga #002 - Saga, Volume 2.cbz", "Saga #003 - Saga, Volume 3.cbz", "series.json"],
            FileNames(SagaFolder));
        Assert.Equal(["Saga"], Directory.GetDirectories(LibraryDir).Select(Path.GetFileName));
        var torrents = await context.Torrents.OrderBy(t => t.Id).ToListAsync();
        Assert.All(torrents, t => Assert.Equal(SagaFolder, t.LibraryPath));
        Assert.Equal("Saga #002 - Saga, Volume 2.cbz", Assert.Single(LinkedFiles(torrents[1])).RelativePath);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(SagaFolder, "series.json")));
        Assert.Equal("Saga", json.RootElement.GetProperty("metadata").GetProperty("name").GetString());
    }

    [Fact]
    public async Task RunAsync_LinkedAgain_FlagsNothing()
    {
        var (context, client) = await LinkThreeVolumesAsync();
        using var _ = context;

        await Runner(context, [], SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.All(await context.Torrents.ToListAsync(), t => Assert.Null(t.LibraryMismatchState));
    }

    [Fact]
    public async Task RunAsync_BookWithoutASeries_GoesIntoTheOneShotsFolder_WithoutSeriesJson()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("standalone.cbz", "one shot")));

        await Runner(context, new() { ["hash1"] = Comic("Some Standalone Graphic Novel") }, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(["Some Standalone Graphic Novel.cbz"], FileNames(Path.Combine(LibraryDir, "_oneshots")));
    }

    [Fact]
    public async Task RunAsync_Pack_KeepsItsOwnFileNames_InTheSeriesFolder()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("Saga Pack/Saga 001.cbz", "issue one"), ("Saga Pack/Saga 002.cbz", "issue two")));

        await Runner(context, new() { ["hash1"] = Comic("Saga, Issues 1-2", 1) }, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(["Saga 001.cbz", "Saga 002.cbz", "series.json"], FileNames(SagaFolder));
    }

    [Fact]
    public async Task RunAsync_AnotherBooksDifferentFileAtTheName_GetsTheNextFreeName_AndIsNotFlagged()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("a.cbz", "first copy")), Torrent("hash2", ("b.cbz", "another, longer copy")));
        var metadata = new Dictionary<string, TorrentMetadata> { ["hash1"] = Comic("Saga, Volume 1", 1), ["hash2"] = Comic("Saga, Volume 1", 1) };

        await Runner(context, metadata, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);
        await Runner(context, metadata, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(["Saga #001 - Saga, Volume 1 (2).cbz", "Saga #001 - Saga, Volume 1.cbz", "series.json"], FileNames(SagaFolder));
        Assert.All(await context.Torrents.ToListAsync(), t => Assert.Null(t.LibraryMismatchState));
    }

    [Fact]
    public async Task RunAsync_AudioOnlyTorrent_IsALinkError()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("book.m4b", "audio")));

        await Runner(context, new() { ["hash1"] = Comic("Saga, Volume 1", 1) with { MediaType = MediaType.Audiobook } }, SeriesLibrary())
            .RunAsync(new QBittorrentConnection(), client);

        Assert.Empty(context.Torrents);
        Assert.Contains("series layout links only ebook formats", (await context.ErroredTorrents.SingleAsync()).ErrorText);
    }

    [Fact]
    public async Task RunAsync_TorrentWithAudioAndAnEbook_LinksOnlyTheEbook()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("book.m4b", "audio"), ("book.cbz", "comic")));

        await Runner(context, new() { ["hash1"] = Comic("Saga, Volume 1", 1) }, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        var torrent = await context.Torrents.SingleAsync();
        Assert.Equal((null, "cbz"), (torrent.ChosenAudioExtension, torrent.ChosenEbookExtension));
        Assert.Equal(["Saga #001 - Saga, Volume 1.cbz", "series.json"], FileNames(SagaFolder));
    }

    [Fact]
    public async Task RunAsync_TorrentWithoutAnAuthor_IsLinked()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("a.cbz", "comic")));

        await Runner(context, new() { ["hash1"] = Comic("Saga, Volume 1", 1, authors: []) }, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(SagaFolder, (await context.Torrents.SingleAsync()).LibraryPath);
        Assert.Empty(context.ErroredTorrents);
    }

    [Fact]
    public async Task EditedTitle_IsFlagged_AndRelinkRenamesTheFile_LeavingTheOtherBooksAlone()
    {
        var (context, client) = await LinkThreeVolumesAsync();
        using var _ = context;
        var second = await context.Torrents.SingleAsync(t => t.Id == "hash2");
        second.MetadataJson = TorrentMetadataSerializer.Serialize(Comic("Saga, Volume Two", 2));
        await context.SaveChangesAsync();

        await Runner(context, [], SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);
        Assert.Equal(nameof(LibraryMismatchState.NewPath), (await context.Torrents.AsNoTracking().SingleAsync(t => t.Id == "hash2")).LibraryMismatchState);

        var result = await Runner(context, [], SeriesLibrary()).RelinkAsync("hash2", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            ["Saga #001 - Saga, Volume 1.cbz", "Saga #002 - Saga, Volume Two.cbz", "Saga #003 - Saga, Volume 3.cbz", "series.json"],
            FileNames(SagaFolder));
        Assert.Null((await context.Torrents.AsNoTracking().SingleAsync(t => t.Id == "hash2")).LibraryMismatchState);
    }

    [Fact]
    public async Task SwitchingALibraryToTheSeriesLayout_FlagsItsBooks_AndRelinkMovesThem()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("Saga v01.cbz", "volume one")));
        var metadata = new Dictionary<string, TorrentMetadata> { ["hash1"] = Comic("Saga, Volume 1", 1) };
        await Runner(context, metadata, BookLibrary()).RunAsync(new QBittorrentConnection(), client);
        var bookFolder = (await context.Torrents.AsNoTracking().SingleAsync()).LibraryPath!;
        Assert.True(File.Exists(Path.Combine(bookFolder, "metadata.json")));

        await Runner(context, metadata, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);
        Assert.Equal(nameof(LibraryMismatchState.NewPath), (await context.Torrents.AsNoTracking().SingleAsync()).LibraryMismatchState);

        var result = await Runner(context, metadata, SeriesLibrary()).RelinkAsync("hash1", [(new QBittorrentConnection(), client)]);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(Directory.Exists(bookFolder));
        Assert.False(Directory.Exists(Path.Combine(LibraryDir, "Brian K. Vaughan")));
        Assert.Equal(["Saga #001 - Saga, Volume 1.cbz", "series.json"], FileNames(SagaFolder));
        Assert.Equal(SagaFolder, (await context.Torrents.AsNoTracking().SingleAsync()).LibraryPath);
    }

    [Fact]
    public async Task RemovingOneBookOfASeriesFolder_LeavesTheOthers_AndTheLastTakesSeriesJsonAndTheFolder()
    {
        var (context, _) = await LinkThreeVolumesAsync();
        using var _ = context;

        foreach (var id in new[] { "hash1", "hash2" })
        {
            var torrent = await context.Torrents.SingleAsync(t => t.Id == id);
            var others = await LibraryFileRemover.FolderUseByOthersAsync(context, SagaFolder, id, CancellationToken.None);
            LibraryFileRemover.Remove(SagaFolder, LinkedFiles(torrent), others.Shared, others.Files, LibraryDir);
            torrent.LibraryPath = null;
            await context.SaveChangesAsync();
        }

        Assert.Equal(["Saga #003 - Saga, Volume 3.cbz", "series.json"], FileNames(SagaFolder));

        var last = await context.Torrents.SingleAsync(t => t.Id == "hash3");
        var lastUse = await LibraryFileRemover.FolderUseByOthersAsync(context, SagaFolder, "hash3", CancellationToken.None);
        LibraryFileRemover.Remove(SagaFolder, LinkedFiles(last), lastUse.Shared, lastUse.Files, LibraryDir);

        Assert.False(Directory.Exists(SagaFolder));
        Assert.True(Directory.Exists(LibraryDir));
    }

    private static TorrentMetadata Issue(string title) => Comic(title) with
    {
        MediaType = MediaType.Ebook,
        Series = [new Series { Name = "Nightfall Stories Magazine", Entries = [] }],
    };

    [Fact]
    public async Task ALoneMagazineIssue_WhoseSeriesHasNoNumber_TakesOneFromItsName_AndIsNotFlaggedAfterwards()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("Nightfall Stories v05n04 (#38) (1981-04) (Tagger).cbr", "issue")));
        var metadata = new Dictionary<string, TorrentMetadata> { ["hash1"] = Issue("Nightfall Stories April 1981") };

        await Runner(context, metadata, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);
        await Runner(context, [], SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        // No numbered issue in the folder yet: by year and month, before volume and number, as a run is.
        Assert.Equal(["Nightfall Stories Magazine #1981.04 - Nightfall Stories April 1981.cbr", "series.json"], FileNames(Path.Combine(LibraryDir, "Nightfall Stories Magazine")));
        Assert.Null((await context.Torrents.AsNoTracking().SingleAsync()).LibraryMismatchState);
    }

    [Fact]
    public async Task ALoneMagazineIssue_IsNumberedAsTheFoldersOtherIssuesAre()
    {
        var folder = Path.Combine(LibraryDir, "Nightfall Stories Magazine");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Nightfall Stories Magazine #005.03 - Nightfall Stories v05n03.cbz"), "another issue");
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("Nightfall Stories v05n04 (1981-04).cbr", "issue")));

        await Runner(context, new() { ["hash1"] = Issue("Nightfall Stories April 1981") }, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Contains("Nightfall Stories Magazine #005.04 - Nightfall Stories April 1981.cbr", FileNames(folder));
    }

    [Fact]
    public async Task ABookWhoseSeriesHasNoNumber_AndWhoseNameIsNoIssue_KeepsItsName()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(Torrent("hash1", ("The Long Winter (Digital).cbz", "book")));

        await Runner(context, new() { ["hash1"] = Issue("The Long Winter") }, SeriesLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(["Nightfall Stories Magazine - The Long Winter.cbz", "series.json"], FileNames(Path.Combine(LibraryDir, "Nightfall Stories Magazine")));
    }
}
