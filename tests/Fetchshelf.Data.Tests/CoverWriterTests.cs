using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class CoverWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-cover-writer-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string DbPath => Path.Combine(_root, "test.db");

    private string LibraryDir => Path.Combine(_root, "library");

    private Library BookLibrary => new() { LibraryDir = LibraryDir, Category = "audiobooks", Method = LibraryLinkMethod.Hardlink };

    /// <summary>A cover source's covers, by MAM id; an id without one has none. Records every id asked for.</summary>
    private sealed class FakeCoverSource(Func<long, CoverImage?> cover) : ICoverSource
    {
        public List<long> Asked { get; } = [];

        public Task<CoverImage?> FetchAsync(Fetchshelf.Data.Entities.TorrentEntity torrent, CancellationToken cancellationToken = default)
        {
            Asked.Add(torrent.MamId);
            return Task.FromResult(cover(torrent.MamId));
        }
    }

    private static readonly CoverWriterOptions Options = new(MaxFetchesPerRun: 20);

    private static CoverImage Jpeg => new([0xFF, 0xD8, 0xFF], "image/jpeg");

    /// <summary>A linked book with MAM id <paramref name="mamId"/> whose folder holds <paramref name="files"/>.</summary>
    private async Task<string> SeedLinkedAsync(FetchshelfDbContext context, long mamId, params string[] files)
    {
        var folder = Path.Combine(LibraryDir, "Author", $"Book {mamId}");
        Directory.CreateDirectory(folder);
        foreach (var file in files.DefaultIfEmpty("01.mp3"))
        {
            await File.WriteAllTextAsync(Path.Combine(folder, file), "x");
        }

        var torrent = await OrganizerTestSupport.SeedTorrentAsync(context, $"hash{mamId}", mamId);
        torrent.LibraryPath = folder;
        await context.SaveChangesAsync();
        return folder;
    }

    private static (int FromFiles, int Fetched, int Waiting) Counts(CoverRunSummary summary) => (summary.FromFiles, summary.Fetched, summary.Waiting);

    private static DateTimeOffset? CheckedAt(FetchshelfDbContext context, long mamId) =>
        context.Torrents.AsNoTracking().Single(t => t.MamId == mamId).CoverCheckedAt;

    [Fact]
    public async Task RunAsync_BookWithoutAnImage_GetsMamsCoverAndIsNotLookedAtAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);

        var summary = await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal((0, 1, 0), Counts(summary));
        Assert.Equal([0xFF, 0xD8, 0xFF], await File.ReadAllBytesAsync(Path.Combine(folder, "cover.jpg")));
        Assert.Equal(["01.mp3", "cover.jpg"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        Assert.NotNull(CheckedAt(context, 1));

        await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();
        Assert.Equal([1L], source.Asked);
    }

    [Fact]
    public async Task RunAsync_PngCover_IsWrittenAsCoverPng()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1);

        await new CoverWriter(context, [BookLibrary], Options, new FakeCoverSource(_ => new CoverImage([1], "image/png"))).RunAsync();

        Assert.True(File.Exists(Path.Combine(folder, "cover.png")));
    }

    [Fact]
    public async Task RunAsync_FolderWithAnImage_AsksMamNothing()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1, "01.mp3", "folder.png");
        var source = new FakeCoverSource(_ => Jpeg);

        var summary = await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal((0, 0, 0), Counts(summary));
        Assert.Empty(source.Asked);
        Assert.False(File.Exists(Path.Combine(folder, "cover.jpg")));
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_MamHasNoCover_WritesNothingAndDoesNotAskAgain()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => null);

        Assert.Equal((0, 0, 0), Counts(await new CoverWriter(context, [BookLibrary], Options, source).RunAsync()));
        await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal(["01.mp3"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Equal([1L], source.Asked);
    }

    [Fact]
    public async Task RunAsync_ManyBooks_FetchesAtMostCoversFromMamAndTheRestNextRun()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        for (var mamId = 1; mamId <= 5; mamId++)
        {
            await SeedLinkedAsync(context, mamId);
        }

        var source = new FakeCoverSource(_ => Jpeg);
        var three = new CoverWriterOptions(MaxFetchesPerRun: 3);

        var first = await new CoverWriter(context, [BookLibrary], three, source).RunAsync();
        var second = await new CoverWriter(context, [BookLibrary], three, source).RunAsync();

        Assert.Equal((0, 3, 2), Counts(first));
        Assert.Equal((0, 2, 0), Counts(second));
        Assert.Equal([1L, 2, 3, 4, 5], source.Asked);
    }

    [Fact]
    public async Task RunAsync_PastTheLimit_SettlesBooksWithAnImage_AndCountsOnlyThoseWithout()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        // More than a page of books, most with an image already; four without, one of them on the second page.
        for (var mamId = 1; mamId <= 502; mamId++)
        {
            await (mamId is 1 or 2 or 3 or 502 ? SeedLinkedAsync(context, mamId) : SeedLinkedAsync(context, mamId, "01.mp3", "cover.jpg"));
        }

        var source = new FakeCoverSource(_ => Jpeg);
        var one = new CoverWriterOptions(MaxFetchesPerRun: 1);

        var first = await new CoverWriter(context, [BookLibrary], one, source).RunAsync();
        var waitingIds = await context.Torrents.AsNoTracking().Where(t => t.CoverCheckedAt == null).Select(t => t.MamId).OrderBy(id => id).ToListAsync();
        var second = await new CoverWriter(context, [BookLibrary], one, source).RunAsync();

        Assert.Equal((0, 1, 3), Counts(first));
        Assert.Equal([2L, 3, 502], waitingIds);
        Assert.Equal((0, 1, 2), Counts(second));
        Assert.Equal([1L, 2], source.Asked);
    }

    [Fact]
    public async Task RunAsync_MamRefusesTheAccount_StopsAndLeavesTheBookForLater()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        await SeedLinkedAsync(context, 2);
        var source = new FakeCoverSource(_ => throw new CoverSourceRefusedException("The source says too many requests."));

        var summary = await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal((0, 0, 2), Counts(summary));
        Assert.Equal([1L], source.Asked);
        Assert.Null(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_FailedRequest_IsLoggedAndTheNextBookStillGetsItsCover()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        var second = await SeedLinkedAsync(context, 2);
        var source = new FakeCoverSource(mamId => mamId == 1 ? throw new HttpRequestException("timed out") : Jpeg);

        var summary = await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal((0, 1, 0), Counts(summary));
        Assert.True(File.Exists(Path.Combine(second, "cover.jpg")));
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Theory]
    [InlineData(LibraryLayout.Series, LibraryLinkMethod.Hardlink)]
    [InlineData(LibraryLayout.Book, LibraryLinkMethod.NoLink)]
    public async Task RunAsync_SeriesOrNoLinkLibrary_IsLeftAlone(LibraryLayout layout, LibraryLinkMethod method)
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);
        var library = new Library { LibraryDir = LibraryDir, Category = "comics", Method = method, Layout = layout };

        await new CoverWriter(context, [library], Options, source).RunAsync();

        Assert.Empty(source.Asked);
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_ABookOfASplitCollection_GetsNoCoverFromMam_WhichIsTheCollections()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        (await context.Torrents.SingleAsync()).CollectionTorrentId = "939c95997a312c59cd9a2e2a89fb47aab0db5848";
        await context.SaveChangesAsync();
        var source = new FakeCoverSource(_ => Jpeg);

        await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task RunAsync_BookOutsideEveryLibrary_IsLeftAlone()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);
        var other = new Library { LibraryDir = Path.Combine(_root, "other"), Category = "ebooks", Method = LibraryLinkMethod.Hardlink };

        await new CoverWriter(context, [other], Options, source).RunAsync();

        Assert.Empty(source.Asked);
    }

    /// <summary>A linked book whose folder holds an EPUB with a cover inside, recorded as its linked file.</summary>
    private async Task<string> SeedWithEpubAsync(FetchshelfDbContext context, long mamId, string? collectionTorrentId = null)
    {
        var folder = Path.Combine(LibraryDir, "Author", $"Epub {mamId}");
        Directory.CreateDirectory(folder);
        BookCoverExtractorTests.WriteEpubWithCover(folder, "book.epub");
        var torrent = await OrganizerTestSupport.SeedTorrentAsync(context, $"epub{mamId}", mamId);
        torrent.LibraryPath = folder;
        torrent.CollectionTorrentId = collectionTorrentId;
        torrent.LinkedFilesJson = System.Text.Json.JsonSerializer.Serialize(new List<LinkedFileRecord> { new("book.epub", 10) });
        await context.SaveChangesAsync();
        return folder;
    }

    [Fact]
    public async Task RunAsync_ACoverInTheBooksOwnFile_IsWritten_WithoutAskingTheSource()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedWithEpubAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);

        var summary = await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal((1, 0, 0), Counts(summary));
        Assert.True(File.Exists(Path.Combine(folder, "cover.jpg")));
        Assert.Empty(source.Asked);
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_WithoutASource_ABookWhoseFilesHaveNoCover_IsSettled_AndOneWithACoverGetsIt()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var plain = await SeedLinkedAsync(context, 1);
        var epub = await SeedWithEpubAsync(context, 2);

        var summary = await new CoverWriter(context, [BookLibrary], Options).RunAsync();

        Assert.Equal((1, 0, 0), Counts(summary));
        Assert.False(File.Exists(Path.Combine(plain, "cover.jpg")));
        Assert.True(File.Exists(Path.Combine(epub, "cover.jpg")));
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_TheFileBudgetSpent_TheRestWait_UnreadAndUnfetched()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var folder = await SeedWithEpubAsync(context, 1);
        var source = new FakeCoverSource(_ => Jpeg);

        var summary = await new CoverWriter(context, [BookLibrary], Options with { FileBudget = TimeSpan.Zero }, source).RunAsync();

        Assert.Equal((0, 0, 1), Counts(summary));
        Assert.False(File.Exists(Path.Combine(folder, "cover.jpg")));
        Assert.Empty(source.Asked);
        Assert.Null(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_ABookOfASplitCollection_GetsTheCoverOfItsOwnFile_ButNoneFetched()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        var withCover = await SeedWithEpubAsync(context, 1, collectionTorrentId: "collection");
        var without = await SeedLinkedAsync(context, 2);
        var book = await context.Torrents.SingleAsync(t => t.MamId == 2);
        book.CollectionTorrentId = "collection";
        await context.SaveChangesAsync();
        var source = new FakeCoverSource(_ => Jpeg);

        await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.True(File.Exists(Path.Combine(withCover, "cover.jpg")));
        Assert.False(File.Exists(Path.Combine(without, "cover.jpg")));
        Assert.Empty(source.Asked);
    }

    /// <summary>A source that can find only some books, defers some, and names itself on the covers it gives.</summary>
    private sealed class PickySource(Func<long, bool> canFind, Func<long, bool> defer) : ICoverSource
    {
        public List<long> Asked { get; } = [];

        public bool CanFind(Fetchshelf.Data.Entities.TorrentEntity torrent) => canFind(torrent.MamId);

        public Task<CoverImage?> FetchAsync(Fetchshelf.Data.Entities.TorrentEntity torrent, CancellationToken cancellationToken = default)
        {
            Asked.Add(torrent.MamId);
            return defer(torrent.MamId)
                ? throw new CoverSourceDeferredException("Its source refused.")
                : Task.FromResult<CoverImage?>(new CoverImage([0xFF, 0xD8, 0xFF], "image/jpeg", "Open Library"));
        }
    }

    [Fact]
    public async Task RunAsync_ABookNoSourceCanFind_IsSettledWithoutAsking_AndDoesntCountTowardsTheLimit()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        await SeedLinkedAsync(context, 2);
        var source = new PickySource(canFind: mamId => mamId == 2, defer: _ => false);

        var summary = await new CoverWriter(context, [BookLibrary], new CoverWriterOptions(MaxFetchesPerRun: 1), source).RunAsync();

        Assert.Equal([2L], source.Asked);
        Assert.Equal((0, 1, 0), (summary.FromFiles, summary.Fetched, summary.Waiting));
        Assert.Equal(1, summary.FetchedFrom["Open Library"]);
        Assert.NotNull(CheckedAt(context, 1));
    }

    [Fact]
    public async Task RunAsync_ADeferredBook_Waits_WhileTheOthersAreStillFetched()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedLinkedAsync(context, 1);
        await SeedLinkedAsync(context, 2);
        var source = new PickySource(canFind: _ => true, defer: mamId => mamId == 1);

        var summary = await new CoverWriter(context, [BookLibrary], Options, source).RunAsync();

        Assert.Equal([1L, 2L], source.Asked);
        Assert.Equal((0, 1, 1), (summary.FromFiles, summary.Fetched, summary.Waiting));
        Assert.Null(CheckedAt(context, 1));
        Assert.NotNull(CheckedAt(context, 2));
    }
}
