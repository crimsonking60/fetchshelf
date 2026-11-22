using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Libraries by genre: books arriving in one qBittorrent category are split between libraries by their genres and main
/// category (`allow_genres`, `deny_genres`, `fiction`), the first library in the file that takes a book getting it.
/// </summary>
public sealed class GenreLibraryLinkTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-genre-library-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");
    private string DownloadsDir => Path.Combine(_root, "downloads");
    private string HistoryDir => Path.Combine(_root, "history");
    private string HistoricalFictionDir => Path.Combine(_root, "historical-fiction");
    private string EbooksDir => Path.Combine(_root, "ebooks");

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b"],
        EbookTypes = ["epub"],
        MusicTypes = ["flac"],
        RadioTypes = ["mp3"],
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class CountingLookup(Dictionary<string, TorrentMetadata> metadata) : IMetadataLookup
    {
        public int Lookups { get; private set; }

        public Task<TorrentMetadata?> TryLookupByInfoHashAsync(string infoHash, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(metadata.GetValueOrDefault(infoHash));
        }
    }

    private static TorrentMetadata Book(string title, FictionCategory? fiction, params GenreCategory[] genres) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Ebook,
        FictionCategory = fiction,
        LegacyCategory = LegacyCategory.EbookHistory,
        GenreCategories = genres.ToHashSet(),
        Language = Language.English,
        Title = title,
        Authors = ["Mary Beard"],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    private static GenreCondition Condition(GenreCategory[] allow, GenreCategory[]? deny = null, FictionCategory? fiction = null) =>
        new(allow.ToHashSet(), (deny ?? []).ToHashSet(), fiction);

    private Library Library(string dir, GenreCondition genres) => new()
    {
        LibraryDir = dir,
        Category = "ebooks",
        Method = LibraryLinkMethod.Hardlink,
        MetadataJson = false,
        Genres = genres,
    };

    /// <summary>The plan's example: History and Historical fiction by genre, then, unless left out, a library for the rest.</summary>
    private List<Library> Libraries(bool withCatchAll = true)
    {
        var libraries = new List<Library>
        {
            Library(HistoryDir, Condition([GenreCategory.Historical, GenreCategory.Politics], fiction: FictionCategory.Nonfiction)),
            Library(HistoricalFictionDir, Condition([GenreCategory.Historical], [GenreCategory.Romance], FictionCategory.Fiction)),
        };
        if (withCatchAll)
        {
            libraries.Add(Library(EbooksDir, GenreCondition.None));
        }

        return libraries;
    }

    private (QBittorrentTorrent Torrent, List<QBittorrentFile> Files) Torrent(string hash)
    {
        var savePath = Path.Combine(DownloadsDir, hash);
        Directory.CreateDirectory(savePath);
        File.WriteAllText(Path.Combine(savePath, $"{hash}.epub"), hash);
        return (new QBittorrentTorrent { Hash = hash, Name = hash, SavePath = savePath, Category = "ebooks", State = "uploading", Progress = 1.0 },
            [new QBittorrentFile { Name = $"{hash}.epub", Size = hash.Length }]);
    }

    private static QBittorrentClient Client(params (QBittorrentTorrent Torrent, List<QBittorrentFile> Files)[] torrents) =>
        OrganizerTestSupport.CreateQBittorrentClient(torrents.Select(t => t.Torrent).ToList(), torrents.ToDictionary(t => t.Torrent.Hash, t => t.Files));

    private static LinkRunner Runner(FetchshelfDbContext context, IMetadataLookup lookup, List<Library> libraries) =>
        new(context, new EventStore(context), lookup, libraries, Formats, new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false));

    private static async Task<Dictionary<string, TorrentEntity>> TorrentsAsync(FetchshelfDbContext context) =>
        await context.Torrents.AsNoTracking().ToDictionaryAsync(t => t.Id);

    [Fact]
    public async Task BooksInOneCategory_GoToTheFirstLibraryWhoseGenresTakeThem()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new()
        {
            ["history"] = Book("SPQR", FictionCategory.Nonfiction, GenreCategory.Historical),
            ["novel"] = Book("Wolf Hall", FictionCategory.Fiction, GenreCategory.Historical),
            ["romance"] = Book("Outlander", FictionCategory.Fiction, GenreCategory.Historical, GenreCategory.Romance),
            ["unsorted"] = Book("A Book", null),
        });

        await Runner(context, lookup, Libraries())
            .RunAsync(new QBittorrentConnection(), Client(Torrent("history"), Torrent("novel"), Torrent("romance"), Torrent("unsorted")));

        var torrents = await TorrentsAsync(context);
        Assert.StartsWith(HistoryDir, torrents["history"].LibraryPath);
        Assert.StartsWith(HistoricalFictionDir, torrents["novel"].LibraryPath);
        // Fiction Historical Romance: neither genre library takes it, so the library without conditions does.
        Assert.StartsWith(EbooksDir, torrents["romance"].LibraryPath);
        // No genres and no main category: only a library without conditions takes it.
        Assert.StartsWith(EbooksDir, torrents["unsorted"].LibraryPath);
    }

    [Fact]
    public async Task NoLibraryTakesItsGenres_NotLinked_SaidOnTheErrorsPage_AndMamNotAskedAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new() { ["romance"] = Book("Outlander", FictionCategory.Fiction, GenreCategory.Historical, GenreCategory.Romance) });
        var client = Client(Torrent("romance"));

        await Runner(context, lookup, Libraries(withCatchAll: false)).RunAsync(new QBittorrentConnection(), client);
        await Runner(context, lookup, Libraries(withCatchAll: false)).RunAsync(new QBittorrentConnection(), client);

        var torrent = (await TorrentsAsync(context))["romance"];
        Assert.Null(torrent.LibraryPath);
        Assert.Equal(nameof(LibraryMismatchState.NoLibrary), torrent.LibraryMismatchState);
        var error = await context.ErroredTorrents.SingleAsync();
        Assert.Equal("No library takes its genres: Fiction; Historical, Romance.", error.ErrorText);
        // The record holds the metadata: the second run read it from there.
        Assert.Equal(1, lookup.Lookups);
    }

    [Fact]
    public async Task OnceALibraryTakesIt_TheNextRunLinksIt_AndClearsTheError()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new() { ["romance"] = Book("Outlander", FictionCategory.Fiction, GenreCategory.Historical, GenreCategory.Romance) });
        var client = Client(Torrent("romance"));
        await Runner(context, lookup, Libraries(withCatchAll: false)).RunAsync(new QBittorrentConnection(), client);

        await Runner(context, lookup, Libraries()).RunAsync(new QBittorrentConnection(), client);

        var torrent = (await TorrentsAsync(context))["romance"];
        Assert.StartsWith(EbooksDir, torrent.LibraryPath);
        Assert.Null(torrent.LibraryMismatchState);
        Assert.Empty(context.ErroredTorrents);
    }

    [Fact]
    public async Task ALinkedBookWhoseGenresNowPickAnotherLibrary_IsADifferentLibrary_AndRelinkMovesIt()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new() { ["history"] = Book("SPQR", FictionCategory.Nonfiction, GenreCategory.Historical) });
        var client = Client(Torrent("history"));
        // Linked before the genre libraries existed.
        await Runner(context, lookup, [Library(EbooksDir, GenreCondition.None)]).RunAsync(new QBittorrentConnection(), client);

        await Runner(context, lookup, Libraries()).RunAsync(new QBittorrentConnection(), client);
        Assert.Equal(nameof(LibraryMismatchState.NewLibraryDir), (await TorrentsAsync(context))["history"].LibraryMismatchState);

        Assert.True((await Runner(context, lookup, Libraries()).RelinkAsync("history", [(new QBittorrentConnection(), client)])).Succeeded);
        var torrent = (await TorrentsAsync(context))["history"];
        Assert.StartsWith(HistoryDir, torrent.LibraryPath);
        Assert.Null(torrent.LibraryMismatchState);
    }

    [Fact]
    public async Task Relink_WhenNoLibraryTakesItsGenres_IsRefusedWithTheReason()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new() { ["romance"] = Book("Outlander", FictionCategory.Fiction, GenreCategory.Historical, GenreCategory.Romance) });
        var client = Client(Torrent("romance"));
        await Runner(context, lookup, Libraries()).RunAsync(new QBittorrentConnection(), client);

        var result = await Runner(context, lookup, Libraries(withCatchAll: false)).RelinkAsync("romance", [(new QBittorrentConnection(), client)]);
        var (blocker, detail) = await Runner(context, lookup, Libraries(withCatchAll: false))
            .RelinkBlockerAsync("romance", [(new QBittorrentConnection(), client)]);

        Assert.False(result.Succeeded);
        Assert.Contains("No library takes its genres: Fiction; Historical, Romance.", result.Message);
        Assert.Equal(RelinkBlocker.NoLibraryTakesGenres, blocker);
        Assert.Equal("No library takes its genres: Fiction; Historical, Romance.", detail);
    }

    [Fact]
    public async Task ATorrentNoLibraryTakesByCategory_CostsNoMamRequest_WhateverTheGenres()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new() { ["history"] = Book("SPQR", FictionCategory.Nonfiction, GenreCategory.Historical) });
        var (torrent, files) = Torrent("history");

        await Runner(context, lookup, Libraries())
            .RunAsync(new QBittorrentConnection(), Client((torrent with { Category = "audiobooks" }, files)));

        Assert.Equal(0, lookup.Lookups);
        Assert.Empty(context.Torrents);
    }

    [Fact]
    public async Task WithMamGenreNames_MetadataJson_HasMamsNames_AndTheMainCategory()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var lookup = new CountingLookup(new() { ["history"] = Book("SPQR", FictionCategory.Nonfiction, GenreCategory.Historical, GenreCategory.Politics) });
        var library = Library(EbooksDir, GenreCondition.None);
        library.MetadataJson = true;
        var runner = new LinkRunner(context, new EventStore(context), lookup, [library], Formats,
            new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false, MamGenreNames: true));

        await runner.RunAsync(new QBittorrentConnection(), Client(Torrent("history")));

        var folder = (await TorrentsAsync(context))["history"].LibraryPath!;
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "metadata.json")));
        var genres = json.RootElement.GetProperty("genres").EnumerateArray().Select(g => g.GetString()).ToList();
        Assert.Contains("Nonfiction", genres);
        Assert.Contains("Politics/Sociology", genres);
        Assert.DoesNotContain("Politics", genres);
    }
}
