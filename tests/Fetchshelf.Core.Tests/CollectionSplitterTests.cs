using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class CollectionSplitterTests
{
    private static readonly IReadOnlyList<string> EbookTypes = ["epub", "azw3", "mobi", "pdf", "cbz", "cbr"];

    private static TorrentMetadata Collection(string? seriesEntries = "1-40", MediaType mediaType = MediaType.Ebook, string? description = null) => new()
    {
        MamId = 339727,
        VipStatus = VipStatus.NotVip,
        MediaType = mediaType,
        LegacyCategory = LegacyCategory.EbookFantasy,
        Language = Language.English,
        Title = "Discworld",
        Authors = ["Terry Pratchett"],
        Series = seriesEntries is null ? [] : [SeriesParser.Parse("Discworld", seriesEntries)],
        Description = description,
        Isbn = "9780000000000",
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    private static List<TorrentFileEntry> Files(params string[] paths) => paths.Select(p => new TorrentFileEntry(p, 100)).ToList();

    [Fact]
    public void CalibreExport_OneBookPerFolder_WithTitleNumberAndCoverFromItsFolder()
    {
        var books = CollectionSplitter.Split(
            Collection(description: "Reading order:\n1. The Colour of Magic\n4. Mort\n34. Thud!"),
            Files(
                "Terry Pratchett - Discworld 1-40/Mort (1019)/Mort - Terry Pratchett.epub",
                "Terry Pratchett - Discworld 1-40/Mort (1019)/cover.jpg",
                "Terry Pratchett - Discworld 1-40/Thud! (949)/Thud! - Terry Pratchett.epub",
                "Terry Pratchett - Discworld 1-40/Thud! (949)/Thud! - Terry Pratchett.mobi",
                "Terry Pratchett - Discworld 1-40/Thud! (949)/cover.jpg"),
            EbookTypes)!;

        Assert.Equal(2, books.Count);
        var (mort, thud) = (books[0], books[1]);
        Assert.Equal((1, "Mort", "4"), (mort.Position, mort.Metadata.Title, mort.Metadata.Series.Single().Entries.Single().Display));
        Assert.Equal("Discworld", mort.Metadata.Series.Single().Name);
        Assert.Equal("Terry Pratchett - Discworld 1-40/Mort (1019)/cover.jpg", mort.CoverFile);
        Assert.Equal(["Terry Pratchett - Discworld 1-40/Thud! (949)/Thud! - Terry Pratchett.epub"], thud.Files);
        Assert.Equal(("epub", "34"), (thud.Format, thud.Metadata.Series.Single().Entries.Single().Display));
        Assert.Null(thud.Metadata.Isbn); // the collection's ISBN and description aren't a book's
        Assert.Null(thud.Metadata.Description);
        Assert.Equal(["Terry Pratchett"], thud.Metadata.Authors);
    }

    [Fact]
    public void EpubDetails_ComeFirst_WhenItsSeriesIsTheCollections()
    {
        var books = CollectionSplitter.Split(
            Collection(),
            Files("D/A (1)/a.epub", "D/B (2)/b.epub"),
            EbookTypes,
            path => path == "D/A (1)/a.epub"
                ? new EpubInfo("Mort", "Discworld", 4, "9780552131063", "Death takes an apprentice.")
                : new EpubInfo("Small Gods", "Something else", 13, null, null))!;

        Assert.Equal(("Mort", "4", "9780552131063"), (books[0].Metadata.Title, books[0].Metadata.Series[0].Entries[0].Display, books[0].Metadata.Isbn));
        Assert.Equal("Death takes an apprentice.", books[0].Metadata.Description);
        Assert.Equal("Small Gods", books[1].Metadata.Title);
        Assert.Empty(books[1].Metadata.Series[0].Entries); // its own series isn't the collection's: no number
    }

    [Fact]
    public void LeadingNumberInTheName_IsTheSeriesNumber()
    {
        var books = CollectionSplitter.Split(Collection(), Files("D/01 - The Colour of Magic.epub", "D/02 - The Light Fantastic.epub"), EbookTypes)!;

        Assert.Equal(
            [("The Colour of Magic", "1"), ("The Light Fantastic", "2")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Entries[0].Display)));
    }

    [Fact]
    public void ATruncatedTitleInTheDescriptionsList_StillGivesItsNumber()
    {
        var books = CollectionSplitter.Split(
            Collection(description: "28. The Amazing Maurice and His Educated Rodents\n4. Mort"),
            Files("D/The Amazing Maurice and His Educated Rod (5)/x.epub", "D/Mort (6)/y.epub"),
            EbookTypes)!;

        Assert.Equal("28", books.Single(b => b.Metadata.Title.StartsWith("The Amazing", StringComparison.Ordinal)).Metadata.Series[0].Entries[0].Display);
    }

    [Fact]
    public void ComicIssuesWithoutARange_AreToldByTheirNumberedNames_EachFormatItsOwnBook()
    {
        var issues = Enumerable.Range(1, 5).Select(i => $"Ultimate Spider-Man/Ultimate Spider-Man {i:000}.cbr").ToList();
        issues.Add("Ultimate Spider-Man/Annuals/Ultimate Spider-Man Annual 03.cbz");

        var books = CollectionSplitter.Split(Collection(seriesEntries: null, mediaType: MediaType.ComicBook), Files([.. issues]), EbookTypes)!;

        Assert.Equal(6, books.Count);
        var annual = books.Single(b => b.Format == "cbz");
        Assert.Equal(("Ultimate Spider-Man Annual 03", "Ultimate Spider-Man Annual", "3"), (annual.Metadata.Title, annual.Metadata.Series[0].Name, annual.Metadata.Series[0].Entries[0].Display));
        var third = books.Single(b => b.Metadata.Title == "Ultimate Spider-Man 003");
        Assert.Equal(("Ultimate Spider-Man", "3", "cbr"), (third.Metadata.Series[0].Name, third.Metadata.Series[0].Entries[0].Display, third.Format));
    }

    [Theory]
    [InlineData("Ultimate Spider-Man 000.5", "Ultimate Spider-Man", "0.5")]
    [InlineData("Ultimate Six 05 - The Ultimates and Ultimate Spider-Man", "Ultimate Six", "5")]
    [InlineData("Ultimatum - Spider-Man Requiem 02", "Ultimatum - Spider-Man Requiem", "2")]
    [InlineData("Saga #12", "Saga", "12")]
    [InlineData("Berserk v07", "Berserk", "7")]
    [InlineData("Berserk Vol. 7", "Berserk", "7")]
    [InlineData("Berserk Volume 07", "Berserk", "7")]
    public void NumberedNames_GiveTheirSeriesAndNumber(string name, string series, string number)
    {
        var paths = new[] { $"C/{name}.cbr", "C/Saga 001.cbr", "C/Saga 002.cbr", "C/Saga 003.cbr" };

        var book = CollectionSplitter.Split(Collection(seriesEntries: null, mediaType: MediaType.ComicBook), Files(paths), EbookTypes)!
            .Single(b => b.Metadata.Title == name);

        Assert.Equal((series, number), (book.Metadata.Series[0].Name, book.Metadata.Series[0].Entries[0].Display));
    }

    [Fact]
    public void VolumesOfARange_GetTheirNumbersFromTheirNames()
    {
        const string Folder = "Reincarnated Into a Game as the Hero's Friend - Running the Kingdom Behind the Scenes v01 - v02";
        var manga = Collection(mediaType: MediaType.Manga) with
        {
            Authors = ["Yuki Suzuki"],
            Series = [SeriesParser.Parse("Reincarnated Into a Game as the Hero's Friend: Running the Kingdom Behind the Scenes", "1-2")],
        };

        var books = CollectionSplitter.Split(
            manga,
            Files(
                $"{Folder}/Reincarnated Into a Game as the Hero's Friend - Running the Kingdom Behind the Scenes v02 - Yuki Suzuki.cbz",
                $"{Folder}/Reincarnated Into a Game as the Hero's Friend - Running the Kingdom Behind the Scenes v01 - Yuki Suzuki.cbz"),
            EbookTypes)!;

        Assert.Equal(
            [("Reincarnated Into a Game as the Hero's Friend - Running the Kingdom Behind the Scenes v01", "1"), ("Reincarnated Into a Game as the Hero's Friend - Running the Kingdom Behind the Scenes v02", "2")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Entries[0].Display)));
        Assert.All(books, b => Assert.Equal(manga.Series[0].Name, b.Metadata.Series[0].Name));
    }

    [Fact]
    public void FewerThanThreeNumberedNames_WithoutARange_IsNotACollection()
    {
        Assert.Null(CollectionSplitter.Split(
            Collection(seriesEntries: null), Files("D/Saga 001.cbz", "D/Saga 002.cbz"), EbookTypes));
    }

    [Fact]
    public void NoRange_AndNamesWithoutNumbers_IsNotACollection()
    {
        Assert.Null(CollectionSplitter.Split(Collection(seriesEntries: "3"), Files("D/Mort.epub", "D/Extras.pdf"), EbookTypes));
    }

    [Fact]
    public void OneBookInTwoFormats_IsNotACollection()
    {
        Assert.Null(CollectionSplitter.Split(Collection(), Files("D/Mort.epub", "D/Mort.mobi"), EbookTypes));
    }

    [Fact]
    public void Audiobooks_AreNotSplit()
    {
        Assert.Null(CollectionSplitter.Split(
            Collection(mediaType: MediaType.Audiobook), Files("D/Disc 1/a.mp3", "D/Disc 2/b.mp3"), ["mp3", "epub"]));
    }

    [Fact]
    public void DiscFolders_AreNotBooks()
    {
        Assert.Null(CollectionSplitter.Split(Collection(), Files("D/Disc 1/a.pdf", "D/Disc 2/b.pdf"), EbookTypes));
    }

    [Fact]
    public void FewerBooksThanTheRange_IsStillSplit_AndAFolderWithoutANumberHasNone()
    {
        var books = CollectionSplitter.Split(Collection(), Files("D/Mort/m.epub", "D/Eric/e.epub"), EbookTypes)!;

        Assert.Equal(["Eric", "Mort"], books.Select(b => b.Metadata.Title));
        Assert.All(books, b => Assert.Empty(b.Metadata.Series[0].Entries));
        Assert.All(books, b => Assert.Null(b.CoverFile));
    }
}
