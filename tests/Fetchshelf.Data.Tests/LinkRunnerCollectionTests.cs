using System.Net;
using System.Net.Http.Json;
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

/// <summary>Collections linked as their books (plan <c>collection-torrents.md</c>).</summary>
public sealed class LinkRunnerCollectionTests : IDisposable
{
    private const string Hash = "939c95997a312c59cd9a2e2a89fb47aab0db5848";

    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-collection-tests-").FullName;

    private static readonly FormatPreferenceLists Formats = new()
    {
        AudioTypes = ["m4b", "mp3"],
        EbookTypes = ["epub", "cbz", "cbr", "pdf"],
        MusicTypes = [],
        RadioTypes = [],
    };

    private string DbPath => Path.Combine(_root, "test.db");

    private string DownloadsDir => Path.Combine(_root, "downloads");

    private string LibraryDir => Path.Combine(_root, "library");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Library EbookLibrary(bool split = true, LibraryLayout layout = LibraryLayout.Book) =>
        new() { Name = "Ebooks", LibraryDir = LibraryDir, Category = "ebooks", Method = LibraryLinkMethod.Hardlink, SplitCollections = split, Layout = layout };

    private LinkRunner Runner(FetchshelfDbContext context, Library library, TorrentMetadata? lookedUp = null) =>
        new(context, new EventStore(context), new FakeMetadataLookup(lookedUp ?? Discworld()), [library], Formats,
            new LinkRunnerOptions(ExcludeNarratorInLibraryDir: false));

    private static TorrentMetadata Discworld() => OrganizerTestSupport.BuildMetadata("Discworld", ["Terry Pratchett"]) with
    {
        MamId = 339727,
        MediaType = MediaType.Ebook,
        LegacyCategory = LegacyCategory.EbookFantasy,
        Series = [SeriesParser.Parse("Discworld", "1-40")],
        Description = "4. Mort\n34. Thud!",
    };

    private static QBittorrentTorrent Torrent(string downloadsDir) =>
        new() { Hash = Hash, Name = "Terry Pratchett - Discworld 1-40", SavePath = downloadsDir, Category = "ebooks", State = "uploading", Progress = 1.0 };

    /// <summary>The Discworld export in the download folder, two books in it, and the files qBittorrent lists.</summary>
    private List<QBittorrentFile> WriteDiscworld()
    {
        string[] paths =
        [
            "Terry Pratchett - Discworld 1-40/Mort (1019)/Mort - Terry Pratchett.epub",
            "Terry Pratchett - Discworld 1-40/Mort (1019)/cover.jpg",
            "Terry Pratchett - Discworld 1-40/Thud! (949)/Thud! - Terry Pratchett.epub",
        ];
        foreach (var path in paths)
        {
            var full = Path.Combine(DownloadsDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }

        return paths.Select(p => new QBittorrentFile { Name = p, Size = 1 }).ToList();
    }

    private QBittorrentClient Client(List<QBittorrentFile> files) =>
        OrganizerTestSupport.CreateQBittorrentClient([Torrent(DownloadsDir)], new Dictionary<string, List<QBittorrentFile>> { [Hash] = files });

    private static string Book(int position) => CollectionBookId.For(Hash, position);

    [Fact]
    public async Task Link_ACollection_IsLinkedAsItsBooks_EachWithItsFolderCoverMetadataAndRecord()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var client = Client(WriteDiscworld());

        var summary = await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), client);

        Assert.Equal(2, summary.Linked);
        var mortFolder = Path.Combine(LibraryDir, "Terry Pratchett", "Discworld", "Discworld #4 - Mort");
        Assert.True(File.Exists(Path.Combine(mortFolder, "Mort - Terry Pratchett.epub")));
        Assert.True(File.Exists(Path.Combine(mortFolder, "cover.jpg")));
        Assert.Contains("\"title\": \"Mort\"", File.ReadAllText(Path.Combine(mortFolder, "metadata.json")));
        Assert.True(Directory.Exists(Path.Combine(LibraryDir, "Terry Pratchett", "Discworld", "Discworld #34 - Thud!")));

        context.ChangeTracker.Clear();
        var collection = await context.Torrents.SingleAsync(t => t.Id == Hash);
        Assert.Null(collection.LibraryPath);
        Assert.Equal(2, collection.CollectionBookCount);
        var mort = await context.Torrents.SingleAsync(t => t.Id == Book(1));
        Assert.Equal(("Mort", Hash, mortFolder, "epub"), (mort.Title, mort.CollectionTorrentId, mort.LibraryPath, mort.ChosenEbookExtension));
        Assert.Equal(2, await context.Events.CountAsync(e => e.Kind == EventKind.Linked));
    }

    [Fact]
    public async Task Link_InALibraryThatDoesNotSplit_LinksTheCollectionAsOneBook()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);

        await Runner(context, EbookLibrary(split: false)).RunAsync(new QBittorrentConnection(), Client(WriteDiscworld()));

        Assert.Equal(Hash, Assert.Single(await context.Torrents.ToListAsync()).Id);
    }

    [Theory]
    [InlineData(true, false, true)] // chosen on the torrent's page, in a library that doesn't split
    [InlineData(false, true, false)] // chosen not to, in a library that splits
    [InlineData(null, true, true)] // no choice: as its library
    public async Task Relink_TheTorrentsOwnChoice_OverridesItsLibrary(bool? choice, bool librarySplits, bool split)
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary(split: false)).RunAsync(new QBittorrentConnection(), Client(files));
        (await context.Torrents.SingleAsync()).SplitCollection = choice;
        await context.SaveChangesAsync();

        var result = await Runner(context, EbookLibrary(librarySplits)).RelinkAsync(Hash, [(new QBittorrentConnection(), Client(files))]);

        Assert.True(result.Succeeded, result.Message);
        context.ChangeTracker.Clear();
        Assert.Equal(split ? 2 : 0, await context.Torrents.CountAsync(t => t.CollectionTorrentId == Hash));
    }

    [Theory]
    [InlineData(true, null, 1)] // linked as one book, and its library splits now: found
    [InlineData(false, null, 0)] // its library doesn't split
    [InlineData(true, false, 0)] // its page says not to split it
    public async Task CollectionsNotSplit_FindsACollectionLinkedAsOneBook_ThatItsLibraryWouldSplit(bool librarySplits, bool? choice, int found)
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary(split: false)).RunAsync(new QBittorrentConnection(), Client(files));
        (await context.Torrents.SingleAsync()).SplitCollection = choice;
        await context.SaveChangesAsync();

        var findings = await Runner(context, EbookLibrary(librarySplits)).CollectionsNotSplitAsync([(new QBittorrentConnection(), Client(files))]);

        Assert.Equal(found, findings.Count);
        if (found > 0)
        {
            var finding = findings.Single();
            Assert.Equal((LibraryFindingKind.CollectionNotSplit, Hash, "Discworld", 2), (finding.Kind, finding.TorrentId, finding.Title, finding.Books));
        }
    }

    [Fact]
    public async Task CollectionsNotSplit_LeavesOutACollectionSplitAlready()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        var runner = Runner(context, EbookLibrary());
        await runner.RunAsync(new QBittorrentConnection(), Client(files));

        Assert.Empty(await runner.CollectionsNotSplitAsync([(new QBittorrentConnection(), Client(files))]));
    }

    [Fact]
    public async Task Link_ASecondRun_LeavesTheBooksAndFollowsTheTorrentsCategory()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        var runner = Runner(context, EbookLibrary());
        await runner.RunAsync(new QBittorrentConnection(), Client(files));

        var second = await runner.RunAsync(new QBittorrentConnection(), Client(files));

        Assert.Equal(0, second.Linked);
        context.ChangeTracker.Clear();
        Assert.All(await context.Torrents.Where(t => t.CollectionTorrentId == Hash).ToListAsync(), b => Assert.Equal("ebooks", b.QBittorrentCategory));
        Assert.Equal(2, await context.Events.CountAsync(e => e.Kind == EventKind.Linked));
    }

    [Fact]
    public async Task Relink_ACollectionLinkedAsOneBook_SplitsIt_AndRemovesTheOldFolder()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary(split: false)).RunAsync(new QBittorrentConnection(), Client(files));
        var oldFolder = (await context.Torrents.SingleAsync()).LibraryPath!;

        var placed = new List<PlacementProgress>();
        var result = await Runner(context, EbookLibrary()).RelinkAsync(Hash, [(new QBittorrentConnection(), Client(files))], placed.Add);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Linked Discworld as 2 books.", result.Message);
        // As each book is placed, for the progress shown.
        Assert.Equal([new PlacementProgress(1, 2, PlacementUnit.Book), new PlacementProgress(2, 2, PlacementUnit.Book)], placed);
        Assert.False(Directory.Exists(oldFolder));
        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.Torrents.CountAsync(t => t.CollectionTorrentId == Hash && t.LibraryPath != null));
    }

    [Fact]
    public async Task Relink_ACollectionAsOneBook_ReportsEachOfItsFilesAsItIsPlaced()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), Client(files));

        var placed = new List<PlacementProgress>();
        var result = await Runner(context, EbookLibrary(split: false)).RelinkAsync(Hash, [(new QBittorrentConnection(), Client(files))], placed.Add);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotEmpty(placed);
        Assert.All(placed, p => Assert.Equal(PlacementUnit.File, p.Unit));
        Assert.Equal(Enumerable.Range(1, placed.Count), placed.Select(p => p.Placed));
        Assert.Equal(placed.Count, placed[^1].Total);
    }

    [Fact]
    public async Task Relink_ABookEditedByHand_IsPlacedByTheEdit()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), Client(files));
        var mort = await context.Torrents.SingleAsync(t => t.Id == Book(1));
        mort.MetadataJson = TorrentMetadataSerializer.Serialize(
            TorrentMetadataSerializer.Deserialize(mort.MetadataJson) with { Title = "Mortality", MetadataSource = MetadataSource.Manual });
        await context.SaveChangesAsync();

        var result = await Runner(context, EbookLibrary()).RelinkAsync(Book(1), [(new QBittorrentConnection(), Client(files))]);

        Assert.True(result.Succeeded, result.Message);
        var editedFolder = Path.Combine(LibraryDir, "Terry Pratchett", "Discworld", "Discworld #4 - Mortality");
        Assert.True(File.Exists(Path.Combine(editedFolder, "Mort - Terry Pratchett.epub")));
        Assert.Contains("\"title\": \"Mortality\"", File.ReadAllText(Path.Combine(editedFolder, "metadata.json")));
        context.ChangeTracker.Clear();
        var relinked = await context.Torrents.SingleAsync(t => t.Id == Book(1));
        Assert.Equal(("Mortality", editedFolder), (relinked.Title, relinked.LibraryPath));
    }

    [Fact]
    public async Task Relink_ABooksId_RelinksItsCollection_AndWithoutSplittingMergesItBack()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), Client(files));
        var mortFolder = (await context.Torrents.SingleAsync(t => t.Id == Book(1))).LibraryPath!;

        var result = await Runner(context, EbookLibrary(split: false)).RelinkAsync(Book(1), [(new QBittorrentConnection(), Client(files))]);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(Directory.Exists(mortFolder));
        context.ChangeTracker.Clear();
        var collection = Assert.Single(await context.Torrents.ToListAsync());
        Assert.Equal((Hash, (int?)null), (collection.Id, collection.CollectionBookCount));
        Assert.NotNull(collection.LibraryPath);
    }

    [Fact]
    public async Task Link_NumberedComicIssues_InASeriesLibrary_AreFilesInTheSeriesFolder()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var paths = Enumerable.Range(1, 3).Select(i => $"Ultimate Spider-Man/Ultimate Spider-Man {i:000}.cbr").Append("Ultimate Spider-Man/Annuals/Ultimate Spider-Man Annual 03.cbz").ToList();
        foreach (var path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(DownloadsDir, path))!);
            File.WriteAllText(Path.Combine(DownloadsDir, path), "x");
        }

        var comics = OrganizerTestSupport.BuildMetadata("Ultimate Spider-Man", ["Brian Michael Bendis"]) with
        {
            MediaType = MediaType.ComicBook,
            LegacyCategory = LegacyCategory.EbookComicsGraphicNovels,
        };
        var client = Client(paths.Select(p => new QBittorrentFile { Name = p, Size = 1 }).ToList());

        await Runner(context, EbookLibrary(layout: LibraryLayout.Series), comics).RunAsync(new QBittorrentConnection(), client);

        Assert.True(File.Exists(Path.Combine(LibraryDir, "Ultimate Spider-Man", "Ultimate Spider-Man #002 - Ultimate Spider-Man 002.cbr")));
        Assert.True(File.Exists(Path.Combine(LibraryDir, "Ultimate Spider-Man Annual", "Ultimate Spider-Man Annual #003 - Ultimate Spider-Man Annual 03.cbz")));
        Assert.Equal(4, await context.Torrents.CountAsync(t => t.CollectionTorrentId == Hash));
    }

    [Fact]
    public async Task Clean_ABookFromACollection_AndASingleOfIt_AreTwoCopies_TheWorseRetired()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), Client(WriteDiscworld()));
        var singleFolder = Path.Combine(LibraryDir, "single", "Mort");
        Directory.CreateDirectory(singleFolder);
        File.WriteAllText(Path.Combine(singleFolder, "Mort.pdf"), "x");
        var single = await OrganizerTestSupport.SeedTorrentAsync(context, "1111111111111111111111111111111111111111", 5000);
        single.MetadataJson = TorrentMetadataSerializer.Serialize(Discworld() with { Title = "Mort", Series = [SeriesParser.Parse("Discworld", "4")], FileTypes = ["pdf"] });
        single.Title = "Mort";
        single.NormalizedTitleKey = TitleNormalizer.Normalize("Mort");
        single.LibraryPath = singleFolder;
        single.ChosenEbookExtension = "pdf";
        single.LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new("Mort.pdf", 1) });
        await context.SaveChangesAsync();

        var summary = await new Cleaner(context, new EventStore(context), Formats, [EbookLibrary()]).RunAsync([]);

        Assert.Equal(1, summary.Retired);
        context.ChangeTracker.Clear();
        Assert.Equal(Book(1), (await context.Torrents.SingleAsync(t => t.Id == single.Id)).ReplacedWithTorrentId);
        Assert.NotNull((await context.Torrents.SingleAsync(t => t.Id == Book(1))).LibraryPath);
    }

    [Fact]
    public async Task Unlink_ACollection_UnlinksEachOfItsBooks()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), Client(WriteDiscworld()));
        var cleaner = new Cleaner(context, new EventStore(context), Formats, [EbookLibrary()]);

        var result = await cleaner.UnlinkAsync(Hash, []);

        Assert.True(result.Succeeded, result.Message);
        Assert.StartsWith("Unlinked its 2 books", result.Message);
        context.ChangeTracker.Clear();
        Assert.All(await context.Torrents.Where(t => t.CollectionTorrentId == Hash).ToListAsync(), b => Assert.Null(b.LibraryPath));
        Assert.False(Directory.Exists(Path.Combine(LibraryDir, "Terry Pratchett")));
    }

    [Fact]
    public async Task Unlink_ABook_AppliesOnUnlinkedOnlyWhenItIsTheCollectionsLastLinkedBook()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var files = WriteDiscworld();
        await Runner(context, EbookLibrary()).RunAsync(new QBittorrentConnection(), Client(files));
        var categorySets = new List<string>();
        var recording = new QBittorrentClient(new HttpClient(new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("auth/login"))
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=abc; path=/");
                return login;
            }

            if (path.Contains("torrents/setCategory"))
            {
                categorySets.Add(request.Content!.ReadAsStringAsync().Result);
            }

            return path.Contains("torrents/info")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { Torrent(DownloadsDir) }) }
                : new HttpResponseMessage(HttpStatusCode.OK);
        })), new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin"));
        var connection = new QBittorrentConnection { OnUnlinked = new QBittorrentCategoryAndTags { Category = "unlinked" } };
        var cleaner = new Cleaner(context, new EventStore(context), Formats, [EbookLibrary()]);

        var first = await cleaner.UnlinkAsync(Book(1), [(connection, recording)]);
        Assert.Empty(categorySets);
        Assert.Contains("not applied: other books of its collection are still linked", first.Message);

        await cleaner.UnlinkAsync(Book(2), [(connection, recording)]);
        Assert.Equal($"hashes={Hash}&category=unlinked", Assert.Single(categorySets));
    }
}
