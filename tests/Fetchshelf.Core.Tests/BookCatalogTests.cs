using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Opds;

namespace Fetchshelf.Core.Tests;

public class BookCatalogTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    internal static TorrentMetadata Metadata(
        string title, string author = "Brandon Sanderson", MediaType mediaType = MediaType.Ebook, IReadOnlyList<Series>? series = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = mediaType,
            LegacyCategory = LegacyCategory.EbookFantasy,
            GenreCategories = new HashSet<GenreCategory> { GenreCategory.Fantasy },
            Language = Language.English,
            Title = title,
            Authors = [author],
            Series = series ?? [],
            MetadataSource = MetadataSource.Mam,
            UploadTime = Day,
            Description = "A book.",
        };

    internal static Series InSeries(string name, decimal number, string display) =>
        new() { Name = name, Entries = [SeriesEntry.ForNumber(number, display)] };

    private static CatalogBook Book(
        string id, string title, string author = "Brandon Sanderson", int daysAgo = 0, int library = 0, IReadOnlyList<Series>? series = null) =>
        CatalogBooks.From(id, Metadata(title, author, series: series), $"/books/{id}", Day.AddDays(-daysAgo), library, null, "epub", [($"{title}.epub", 100)])!;

    private static CatalogBook Audiobook(string id, string title, int library = 0) =>
        CatalogBooks.From(id, Metadata(title, mediaType: MediaType.Audiobook), $"/books/{id}", Day, library, "m4b", null, [($"{title}.m4b", 100)])!;

    [Fact]
    public void From_EbookWithItsFormat_KeepsOnlyFilesOfThatFormat()
    {
        var book = CatalogBooks.From(
            "hash", Metadata("Dune", series: [InSeries("Dune", 1, "1")]), "/books/dune", Day, 2, null, "EPUB",
            [("Dune.epub", 300), ("Dune.pdf", 900), ("cover.jpg", 10)])!;

        Assert.Equal([new CatalogFile("Dune.epub", 300, "epub")], book.EbookFiles);
        Assert.Empty(book.AudioFiles);
        Assert.Equal([new CatalogSeries("Dune", 1, "1")], book.Series);
        Assert.Equal(2, book.LibraryIndex);
        Assert.True(book.MayHaveCover);
    }

    [Fact]
    public void From_AudiobookWithCompanionPdf_HasBothKindsOfFile()
    {
        var book = CatalogBooks.From(
            "hash", Metadata("Dune", mediaType: MediaType.Audiobook), "/b", Day, 0, "mp3", "pdf",
            [("Dune 02.mp3", 2), ("Dune 01.mp3", 1), ("Dune.pdf", 5), ("cover.jpg", 1)])!;

        Assert.Equal(["Dune 01.mp3", "Dune 02.mp3"], book.AudioFiles.Select(file => file.RelativePath));
        Assert.Equal(["Dune.pdf"], book.EbookFiles.Select(file => file.RelativePath));
        Assert.Equal(MediaType.Audiobook, book.MediaType);
        Assert.Equal("Dune 01.mp3", book.EmbeddedCoverFile?.RelativePath);
    }

    [Theory]
    [InlineData("m4b", null, "Book.m4b")]
    [InlineData("mp3", "epub", "Book.epub")]
    [InlineData("flac", "pdf", null)]
    [InlineData(null, "cbz", "Book.cbz")]
    [InlineData(null, "cbr", "Book.cbr")]
    public void EmbeddedCoverFile_IsTheFirstFileOfAFormatThatCanHoldOne(string? audio, string? ebook, string? expected)
    {
        var files = new[] { audio, ebook }.OfType<string>().Select(extension => ($"Book.{extension}", 1L));
        var book = CatalogBooks.From("hash", Metadata("Book"), "/b", Day, 0, audio, ebook, files)!;

        Assert.Equal(expected, book.EmbeddedCoverFile?.RelativePath);
    }

    [Fact]
    public void MayHaveCover_InItsOwnFolderAlways_InASharedOneOnlyWithAnEmbeddedCover()
    {
        var pdf = CatalogBooks.From("hash", Metadata("Book"), "/b", Day, 0, null, "pdf", [("Book.pdf", 1)])!;
        var epub = CatalogBooks.From("hash", Metadata("Book"), "/b", Day, 0, null, "epub", [("Book.epub", 1)])!;

        Assert.True(pdf.MayHaveCover);
        Assert.False((pdf with { HasOwnFolder = false }).MayHaveCover);
        Assert.True((epub with { HasOwnFolder = false }).MayHaveCover);
    }

    [Fact]
    public void From_NoFileOfALinkedFormat_IsLeftOut()
    {
        Assert.Null(CatalogBooks.From("hash", Metadata("Dune"), "/b", Day, 0, null, "epub", [("Dune.pdf", 1)]));
        Assert.Null(CatalogBooks.From("hash", Metadata("Dune"), "/b", Day, 0, "m4b", null, [("Dune.mp3", 1)]));
        Assert.Null(CatalogBooks.From("hash", Metadata("Dune"), "/b", Day, 0, null, null, [("Dune.epub", 1)]));
    }

    [Fact]
    public void From_SeveralParts_KeepsThemAllInNumberOrder()
    {
        var book = CatalogBooks.From(
            "hash", Metadata("Big", mediaType: MediaType.Audiobook), "/b", Day, 0, "mp3", null,
            [("Disc 10/01.mp3", 4), ("Part 10.mp3", 3), ("Disc 2/01.mp3", 5), ("part 2.mp3", 2), ("Part 1.mp3", 1)])!;

        Assert.Equal(
            ["Disc 2/01.mp3", "Disc 10/01.mp3", "Part 1.mp3", "part 2.mp3", "Part 10.mp3"],
            book.AudioFiles.Select(file => file.RelativePath));
    }

    [Theory]
    [InlineData(MediaType.Audiobook)]
    [InlineData(MediaType.Musicology)]
    [InlineData(MediaType.PeriodicalAudiobook)]
    public void Opds_NotAnEbookMediaType_IsLeftOut(MediaType mediaType)
    {
        var book = CatalogBooks.From("hash", Metadata("Dune", mediaType: mediaType), "/b", Day, 0, "mp3", "pdf", [("Dune.pdf", 1), ("Dune.mp3", 1)])!;

        Assert.False(OpdsCatalog.Serves(book));
    }

    [Fact]
    public void Opds_ServesOnlyEbooksInTheServedLibraries()
    {
        var catalog = new BookCatalog(
            [Book("a", "Novel", library: 0), Book("b", "Comic", library: 2), Audiobook("c", "Listen", library: 0)],
            [new CatalogLibrary(0, "Mixed"), new CatalogLibrary(1, "Empty"), new CatalogLibrary(2, "Comics")]);

        Assert.Equal(["Comic", "Novel"], OpdsCatalog.From(catalog, servedLibraries: null).Books.Select(book => book.Title).Order());
        var mixedOnly = OpdsCatalog.From(catalog, new HashSet<int> { 0 });
        Assert.Equal(["Novel"], mixedOnly.Books.Select(book => book.Title));
        Assert.Equal(["Mixed"], mixedOnly.Libraries.Select(library => library.Name));
        Assert.Equal(3, catalog.Books.Count);
    }

    [Fact]
    public void Recent_IsNewestFirst()
    {
        var catalog = new BookCatalog([Book("a", "Old", daysAgo: 5), Book("b", "New", daysAgo: 0), Book("c", "Middle", daysAgo: 2)], []);

        Assert.Equal(["New", "Middle", "Old"], catalog.Recent().Select(book => book.Title));
    }

    [Fact]
    public void Authors_AreAToZWithTheirBookCounts()
    {
        var catalog = new BookCatalog([Book("a", "One", "zadie Smith"), Book("b", "Two", "Ann Leckie"), Book("c", "Three", "Ann Leckie")], []);

        Assert.Equal([new CatalogGroup("Ann Leckie", 2), new CatalogGroup("zadie Smith", 1)], catalog.Authors());
    }

    [Fact]
    public void ByAuthor_PutsSeriesInOrderFirst_ThenOtherBooksByTitle()
    {
        var catalog = new BookCatalog(
        [
            Book("a", "Standalone"),
            Book("b", "Words of Radiance", series: [InSeries("Stormlight Archive", 2, "2")]),
            Book("c", "The Way of Kings", series: [InSeries("Stormlight Archive", 1, "1")]),
            Book("d", "Another Standalone"),
            Book("e", "Someone Else's", author: "Ann Leckie"),
        ], []);

        Assert.Equal(
            ["The Way of Kings", "Words of Radiance", "Another Standalone", "Standalone"],
            catalog.ByAuthor("Brandon Sanderson").Select(book => book.Title));
    }

    [Fact]
    public void Series_AndBySeries_ListBooksInSeriesOrder()
    {
        var catalog = new BookCatalog(
        [
            Book("a", "Book 10", series: [InSeries("Saga", 10, "10")]),
            Book("b", "Book 2", series: [InSeries("Saga", 2, "2")]),
            Book("c", "Novella", series: [InSeries("Saga", 2.5m, "2.5")]),
            Book("d", "Other", series: [InSeries("Other Series", 1, "1")]),
        ], []);

        Assert.Equal([new CatalogGroup("Other Series", 1), new CatalogGroup("Saga", 3)], catalog.Series());
        Assert.Equal(["Book 2", "Novella", "Book 10"], catalog.BySeries("Saga").Select(book => book.Title));
    }

    [Fact]
    public void Search_MatchesEveryWordInTitleAuthorOrSeries_IgnoringCaseAndAccents()
    {
        var catalog = new BookCatalog(
        [
            Book("a", "Jane Eyre", "Charlotte Brontë"),
            Book("b", "Wuthering Heights", "Emily Brontë"),
            Book("c", "Mistborn", series: [InSeries("Mistborn Era One", 1, "1")]),
        ], []);

        Assert.Equal(["Jane Eyre"], catalog.Search("bronte jane").Select(book => book.Title));
        Assert.Equal(["Mistborn"], catalog.Search("era one").Select(book => book.Title));
        Assert.Empty(catalog.Search("   "));
    }

    [Fact]
    public void Libraries_OnlyThoseWithBooks_AndByLibraryIsNewestFirst()
    {
        var catalog = new BookCatalog(
            [Book("a", "Comic", library: 2, daysAgo: 3), Book("b", "Novel", library: 0), Book("c", "Comic 2", library: 2)],
            [new CatalogLibrary(0, "Ebooks"), new CatalogLibrary(1, "Empty"), new CatalogLibrary(2, "Comics")]);

        Assert.Equal(["Ebooks", "Comics"], catalog.Libraries.Select(library => library.Name));
        Assert.Equal(["Comic 2", "Comic"], catalog.ByLibrary(2).Select(book => book.Title));
    }

    [Fact]
    public void ForLibrary_HasOnlyThatLibrarysBooks_AndIsNullForOneWithoutBooks()
    {
        var catalog = new BookCatalog(
            [Book("a", "Comic", library: 2), Book("b", "Novel", library: 0)],
            [new CatalogLibrary(0, "Ebooks"), new CatalogLibrary(1, "Empty"), new CatalogLibrary(2, "Comics")]);

        var comics = catalog.ForLibrary(2)!;

        Assert.Equal(["Comic"], comics.Recent().Select(book => book.Title));
        Assert.Equal("Comics", Assert.Single(comics.Libraries).Name);
        Assert.Same(comics, catalog.ForLibrary(2));
        Assert.Null(catalog.ForLibrary(1));
        Assert.Null(catalog.ForLibrary(5));
    }

    [Fact]
    public void Find_IgnoresTheCaseOfTheHash()
    {
        var catalog = new BookCatalog([Book("abcdef", "Dune")], []);

        Assert.Equal("Dune", catalog.Find("ABCDEF")?.Title);
        Assert.Null(catalog.Find("other"));
    }
}
