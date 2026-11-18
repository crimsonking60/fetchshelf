using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>`language_in_folder`: two editions of a title in two languages don't share a library folder.</summary>
public sealed class LanguageInFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-language-in-folder-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");
    private string DownloadsDir => Path.Combine(_root, "downloads");
    private string LibraryDir => Path.Combine(_root, "library");
    private string AuthorFolder => Path.Combine(LibraryDir, "Frank Herbert");

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b"],
        EbookTypes = ["cbz", "epub"],
        MusicTypes = ["flac"],
        RadioTypes = ["mp3"],
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class ByHashLookup(Dictionary<string, TorrentMetadata> metadata) : IMetadataLookup
    {
        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(metadata.GetValueOrDefault(infoHash));
    }

    private static TorrentMetadata Book(string title, Language language, string? series = null, MediaType mediaType = MediaType.Ebook) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = mediaType,
        LegacyCategory = LegacyCategory.EbookGeneralFiction,
        Language = language,
        Title = title,
        Authors = ["Frank Herbert"],
        Series = series is null ? [] : [new Series { Name = series, Entries = [SeriesEntry.ForNumber(1, "1")] }],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    private Library Library(LanguageInFolder languageInFolder = LanguageInFolder.OnClash, LibraryLayout layout = LibraryLayout.Book) => new()
    {
        LibraryDir = LibraryDir,
        Category = "books",
        Method = LibraryLinkMethod.Hardlink,
        LanguageInFolder = languageInFolder,
        Layout = layout,
        MetadataJson = layout == LibraryLayout.Book,
    };

    private (QBittorrentTorrent Torrent, List<QBittorrentFile> Files) Torrent(string hash, string file)
    {
        var savePath = Path.Combine(DownloadsDir, hash);
        Directory.CreateDirectory(savePath);
        File.WriteAllText(Path.Combine(savePath, file), hash);
        return (new QBittorrentTorrent { Hash = hash, Name = hash, SavePath = savePath, Category = "books", State = "uploading", Progress = 1.0 },
            [new QBittorrentFile { Name = file, Size = hash.Length }]);
    }

    private static QBittorrentClient Client(params (QBittorrentTorrent Torrent, List<QBittorrentFile> Files)[] torrents) =>
        OrganizerTestSupport.CreateQBittorrentClient(torrents.Select(t => t.Torrent).ToList(), torrents.ToDictionary(t => t.Torrent.Hash, t => t.Files));

    private static LinkRunner Runner(FetchshelfDbContext context, Dictionary<string, TorrentMetadata> metadata, Library library) =>
        new(context, new EventStore(context), new ByHashLookup(metadata), [library], Formats,
            new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false, FirstLanguage: Language.English));

    private static async Task<Dictionary<string, TorrentEntity>> TorrentsAsync(FetchshelfDbContext context) =>
        await context.Torrents.AsNoTracking().ToDictionaryAsync(t => t.Id);

    [Theory]
    [InlineData("english", "greek")]
    [InlineData("greek", "english")]
    public async Task OnClash_TheSecondEditionGetsItsLanguage_AndNeitherIsFlagged(string first, string second)
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = new Dictionary<string, TorrentMetadata> { ["english"] = Book("Gamma", Language.English), ["greek"] = Book("Gamma", Language.Greek) };
        var languages = new Dictionary<string, string> { ["english"] = "English", ["greek"] = "Greek" };

        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), Client(Torrent(first, "a.epub")));
        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), Client(Torrent(first, "a.epub"), Torrent(second, "b.epub")));
        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), Client(Torrent(first, "a.epub"), Torrent(second, "b.epub")));

        var torrents = await TorrentsAsync(context);
        Assert.Equal(Path.Combine(AuthorFolder, "Gamma"), torrents[first].LibraryPath);
        Assert.Equal(Path.Combine(AuthorFolder, $"Gamma ({languages[second]})"), torrents[second].LibraryPath);
        Assert.All(torrents.Values, t => Assert.Null(t.LibraryMismatchState));
        Assert.True(File.Exists(Path.Combine(torrents[second].LibraryPath!, "b.epub")));
    }

    [Fact]
    public async Task OnClash_TheSameLanguage_IsNoClash()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = new Dictionary<string, TorrentMetadata> { ["one"] = Book("Gamma", Language.English), ["two"] = Book("Gamma", Language.English) };

        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), Client(Torrent("one", "a.epub"), Torrent("two", "b.epub")));

        Assert.All((await TorrentsAsync(context)).Values, t => Assert.Equal(Path.Combine(AuthorFolder, "Gamma"), t.LibraryPath));
    }

    [Fact]
    public async Task Other_MarksEveryBookNotInTheFirstLanguage()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = new Dictionary<string, TorrentMetadata> { ["english"] = Book("Dune", Language.English), ["hungarian"] = Book("A dűne", Language.Hungarian) };

        await Runner(context, metadata, Library(LanguageInFolder.Other))
            .RunAsync(new QBittorrentConnection(), Client(Torrent("english", "a.epub"), Torrent("hungarian", "b.epub")));

        var torrents = await TorrentsAsync(context);
        Assert.Equal(Path.Combine(AuthorFolder, "Dune"), torrents["english"].LibraryPath);
        Assert.Equal(Path.Combine(AuthorFolder, "A dűne (Hungarian)"), torrents["hungarian"].LibraryPath);
    }

    [Fact]
    public async Task Other_InASeriesLibrary_MarksTheFileName()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = new Dictionary<string, TorrentMetadata>
        {
            ["english"] = Book("Saga, Volume 1", Language.English, "Saga", MediaType.ComicBook),
            ["hungarian"] = Book("Saga, Volume 1", Language.Hungarian, "Saga", MediaType.ComicBook),
        };

        await Runner(context, metadata, Library(LanguageInFolder.Other, LibraryLayout.Series))
            .RunAsync(new QBittorrentConnection(), Client(Torrent("english", "a.cbz"), Torrent("hungarian", "b.cbz")));

        Assert.Equal(
            ["Saga #001 - Saga, Volume 1 (Hungarian).cbz", "Saga #001 - Saga, Volume 1.cbz", "series.json"],
            Directory.GetFiles(Path.Combine(LibraryDir, "Saga")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EditionsMergedInOneFolderBefore_AreFlagged_AndRelinkSeparatesThem()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var metadata = new Dictionary<string, TorrentMetadata> { ["english"] = Book("Gamma", Language.English), ["greek"] = Book("Gamma", Language.Greek) };
        var client = Client(Torrent("english", "a.epub"), Torrent("greek", "b.epub"));
        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), client);
        // As an earlier version linked them: the Greek edition in the English one's folder.
        var plain = Path.Combine(AuthorFolder, "Gamma");
        var greek = await context.Torrents.SingleAsync(t => t.Id == "greek");
        File.Move(Path.Combine(greek.LibraryPath!, "b.epub"), Path.Combine(plain, "b.epub"));
        Directory.Delete(greek.LibraryPath!, recursive: true);
        greek.LibraryPath = plain;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), client);
        Assert.All((await TorrentsAsync(context)).Values, t => Assert.Equal(nameof(LibraryMismatchState.NewPath), t.LibraryMismatchState));

        Assert.True((await Runner(context, metadata, Library()).RelinkAsync("greek", [(new QBittorrentConnection(), client)])).Succeeded);
        await Runner(context, metadata, Library()).RunAsync(new QBittorrentConnection(), client);

        var torrents = await TorrentsAsync(context);
        Assert.Equal(plain, torrents["english"].LibraryPath);
        Assert.Equal(Path.Combine(AuthorFolder, "Gamma (Greek)"), torrents["greek"].LibraryPath);
        Assert.All(torrents.Values, t => Assert.Null(t.LibraryMismatchState));
        Assert.Equal(["a.epub", "metadata.json"], Directory.GetFiles(plain).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
