using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Opds;

namespace Fetchshelf.Core.Tests;

public class OpdsCatalogTests
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

    private static OpdsBook Book(
        string id, string title, string author = "Brandon Sanderson", int daysAgo = 0, int library = 0, IReadOnlyList<Series>? series = null) =>
        OpdsBooks.From(id, Metadata(title, author, series: series), $"/books/{id}", Day.AddDays(-daysAgo), library, "epub", [($"{title}.epub", 100)])!;

    [Fact]
    public void From_EbookWithItsFormat_KeepsOnlyFilesOfThatFormat()
    {
        var book = OpdsBooks.From(
            "hash", Metadata("Dune", series: [InSeries("Dune", 1, "1")]), "/books/dune", Day, 2, "EPUB",
            [("Dune.epub", 300), ("Dune.pdf", 900), ("cover.jpg", 10)])!;

        Assert.Equal([new OpdsFile("Dune.epub", 300, "epub")], book.Files);
        Assert.Equal([new OpdsSeries("Dune", 1, "1")], book.Series);
        Assert.Equal(2, book.LibraryIndex);
        Assert.True(book.MayHaveCover);
    }

    [Theory]
    [InlineData(MediaType.Audiobook)]
    [InlineData(MediaType.Musicology)]
    [InlineData(MediaType.PeriodicalAudiobook)]
    public void From_NotAnEbookMediaType_IsLeftOut(MediaType mediaType) =>
        Assert.Null(OpdsBooks.From("hash", Metadata("Dune", mediaType: mediaType), "/b", Day, 0, "pdf", [("Dune.pdf", 1)]));

    [Fact]
    public void From_NoFileOfTheEbookFormat_IsLeftOut() =>
        Assert.Null(OpdsBooks.From("hash", Metadata("Dune"), "/b", Day, 0, "epub", [("Dune.pdf", 1)]));

    [Fact]
    public void From_SeveralParts_KeepsThemAllInNameOrder()
    {
        var book = OpdsBooks.From("hash", Metadata("Big"), "/b", Day, 0, "pdf", [("Part 2.pdf", 2), ("Part 1.pdf", 1)])!;

        Assert.Equal(["Part 1.pdf", "Part 2.pdf"], book.Files.Select(file => file.RelativePath));
        Assert.False(book.MayHaveCover);
    }

    [Fact]
    public void Recent_IsNewestFirst()
    {
        var catalog = new OpdsCatalog([Book("a", "Old", daysAgo: 5), Book("b", "New", daysAgo: 0), Book("c", "Middle", daysAgo: 2)], []);

        Assert.Equal(["New", "Middle", "Old"], catalog.Recent().Select(book => book.Title));
    }

    [Fact]
    public void Authors_AreAToZWithTheirBookCounts()
    {
        var catalog = new OpdsCatalog([Book("a", "One", "zadie Smith"), Book("b", "Two", "Ann Leckie"), Book("c", "Three", "Ann Leckie")], []);

        Assert.Equal([new OpdsGroup("Ann Leckie", 2), new OpdsGroup("zadie Smith", 1)], catalog.Authors());
    }

    [Fact]
    public void ByAuthor_PutsSeriesInOrderFirst_ThenOtherBooksByTitle()
    {
        var catalog = new OpdsCatalog(
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
        var catalog = new OpdsCatalog(
        [
            Book("a", "Book 10", series: [InSeries("Saga", 10, "10")]),
            Book("b", "Book 2", series: [InSeries("Saga", 2, "2")]),
            Book("c", "Novella", series: [InSeries("Saga", 2.5m, "2.5")]),
            Book("d", "Other", series: [InSeries("Other Series", 1, "1")]),
        ], []);

        Assert.Equal([new OpdsGroup("Other Series", 1), new OpdsGroup("Saga", 3)], catalog.Series());
        Assert.Equal(["Book 2", "Novella", "Book 10"], catalog.BySeries("Saga").Select(book => book.Title));
    }

    [Fact]
    public void Search_MatchesEveryWordInTitleAuthorOrSeries_IgnoringCaseAndAccents()
    {
        var catalog = new OpdsCatalog(
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
        var catalog = new OpdsCatalog(
            [Book("a", "Comic", library: 2, daysAgo: 3), Book("b", "Novel", library: 0), Book("c", "Comic 2", library: 2)],
            [new OpdsLibrary(0, "Ebooks"), new OpdsLibrary(1, "Empty"), new OpdsLibrary(2, "Comics")]);

        Assert.Equal(["Ebooks", "Comics"], catalog.Libraries.Select(library => library.Name));
        Assert.Equal(["Comic 2", "Comic"], catalog.ByLibrary(2).Select(book => book.Title));
    }

    [Fact]
    public void ForLibrary_HasOnlyThatLibrarysBooks_AndIsNullForOneWithoutBooks()
    {
        var catalog = new OpdsCatalog(
            [Book("a", "Comic", library: 2), Book("b", "Novel", library: 0)],
            [new OpdsLibrary(0, "Ebooks"), new OpdsLibrary(1, "Empty"), new OpdsLibrary(2, "Comics")]);

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
        var catalog = new OpdsCatalog([Book("abcdef", "Dune")], []);

        Assert.Equal("Dune", catalog.Find("ABCDEF")?.Title);
        Assert.Null(catalog.Find("other"));
    }
}
