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
    [InlineData("Ultimates 2 001", "Ultimates 2", "1")]
    [InlineData("Ultimate X4 002", "Ultimate X4", "2")]
    [InlineData("Saga #12", "Saga", "12")]
    [InlineData("Berserk v07", "Berserk", "7")]
    [InlineData("Berserk Vol. 7", "Berserk", "7")]
    [InlineData("Berserk Volume 07", "Berserk", "7")]
    [InlineData("Popular Mechanics Shop Notes.Vol.13.1917", "Popular Mechanics Shop Notes.", "13")]
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
    public void TrailingYearsAndReleaseTagsInBrackets_AreLeftOutOfTheTitleAndNumber()
    {
        const string Folder = "American Vampire (v01-v07)(2010-2015) (digital-Empire)";
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Authors = ["Scott Snyder"] },
            Files(
                $"{Folder}/American Vampire v06 (2014) (Digital) (Zone-Empire).cbr",
                $"{Folder}/American Vampire v05 (2013) (digital) (X-MAS 2013-Empire).cbr",
                $"{Folder}/American Vampire v01 (2010) (digital) (X-MAS_2013-Empire).cbr",
                $"{Folder}/American Vampire v02 [2011] [scan].cbr"),
            EbookTypes)!;

        Assert.Equal(
            [("American Vampire v01", "1"), ("American Vampire v02", "2"), ("American Vampire v05", "5"), ("American Vampire v06", "6")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Entries[0].Display)).OrderBy(b => b.Item2));
        Assert.All(books, b => Assert.Equal("American Vampire", b.Metadata.Series[0].Name));
    }

    [Fact]
    public void IssuesNamedAfterAVolume_TakeTheTorrentsBetterWordedTitleAsTheirSeries()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Batman: Black and White", Authors = ["Neil Gaiman"] },
            Files(Enumerable.Range(1, 4).Select(i => $"Batman - Black and White/Black  White Vol 1 #{i:00}.cbr").ToArray()),
            EbookTypes)!;

        Assert.Equal(
            [("Batman: Black and White Vol 1 #01", "Batman: Black and White Vol 1", "1"), ("Batman: Black and White Vol 1 #04", "Batman: Black and White Vol 1", "4")],
            new[] { books[0], books[3] }.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Name, b.Metadata.Series[0].Entries[0].Display)));
    }

    [Fact]
    public void TheTorrentsTitle_IsNotTakenForASeriesWithWordsItLacks()
    {
        var names = new[] { "Ultimate Six 01", "Ultimate Six 02", "Ultimate Six 03", "Ultimate Spider-Man 001" };

        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Ultimate Spider-Man" },
            Files(names.Select(n => $"C/{n}.cbr").ToArray()),
            EbookTypes)!;

        Assert.Equal(["Ultimate Six", "Ultimate Six", "Ultimate Six", "Ultimate Spider-Man"], books.Select(b => b.Metadata.Series[0].Name));
        Assert.Equal("Ultimate Six 01", books[0].Metadata.Title);
    }

    [Fact]
    public void ASeriesSpelledSeveralWays_IsNamedAsMostOfItsFilesSpellIt()
    {
        var names = new[] { "Ultimate Spider-Man Annual 01", "Ultimate Spider-man Annual 02", "Ultimate Spider-Man Annual 03", "Miles Morales - Ultimate Spider-Man 007" };

        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Marvel Ultimate Universe Comic Collection" },
            Files([.. names.Select(n => $"C/{n}.cbr"), "C/Miles Morales Ultimate Spider-Man 001.cbr", "C/Miles Morales Ultimate Spider-Man 002.cbr"]),
            EbookTypes)!;

        Assert.Equal(
            ["Miles Morales Ultimate Spider-Man", "Miles Morales Ultimate Spider-Man", "Miles Morales Ultimate Spider-Man", "Ultimate Spider-Man Annual", "Ultimate Spider-Man Annual", "Ultimate Spider-Man Annual"],
            books.Select(b => b.Metadata.Series[0].Name));
        Assert.Equal("Ultimate Spider-Man Annual 02", books.Single(b => b.Metadata.Series[0].Entries[0].Display == "2" && b.Metadata.Title.Contains("Annual")).Metadata.Title);
    }

    [Fact]
    public void UnderscoredNames_AreReadAsSpacedOnes_WithoutWhatFollowsTheNumber()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "The Umbrella Academy: Dallas (1-6) [CBR]", Authors = ["Gerard Way"] },
            Files(
                "The Umbrella Academy - Dallas/Umbrella_Academy_-_Dallas_001_of_06_Both_Covers_2008_Minutemen-BroomhandleMauser_Locke.cbr",
                "The Umbrella Academy - Dallas/Umbrella_Academy_-_Dallas_002__2008___Steam-DCP_.cbr",
                "The Umbrella Academy - Dallas/Umbrella_Academy_-_Dallas_006__2009___Steam_.cbr"),
            EbookTypes)!;

        Assert.Equal(
            [("The Umbrella Academy: Dallas 001", "1"), ("The Umbrella Academy: Dallas 002", "2"), ("The Umbrella Academy: Dallas 006", "6")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Entries[0].Display)));
        Assert.All(books, b => Assert.Equal("The Umbrella Academy: Dallas", b.Metadata.Series[0].Name));
    }

    [Fact]
    public void FoldersOfIssues_AreSplitIntoTheirIssues_EachVolumeItsOwnSeries()
    {
        const string Top = "Daredevil Collection (1964-2016)";
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Daredevil Collection (1964-2016)", Authors = ["Marvel Comics"] },
            Files(
                $"{Top}/Daredevil v1 01-380+Annuals (1964-1998)/Daredevil v1 001 (1964) (digital) (G85-Empire).cbr",
                $"{Top}/Daredevil v1 01-380+Annuals (1964-1998)/Daredevil v1 002 (1964) (digital) (G85-Empire).cbr",
                $"{Top}/Daredevil v1 01-380+Annuals (1964-1998)/Daredevil v1 051 (1969) marvel apr- 36p c2c.cbr",
                $"{Top}/Daredevil v1 01-380+Annuals (1964-1998)/Annuals/Daredevil v1 Annual 001 (1967) (Digital) (AnHeroGold-Empire).cbz",
                $"{Top}/Daredevil v1 01-380+Annuals (1964-1998)/Annuals/Daredevil v1 Annual 001 (1967).cbr",
                $"{Top}/Daredevil v2 ½-119+500-512 &Extras (1998-2009)/Daredevil 001 (1998) (digital) (Son of Ultron-Empire).cbr",
                $"{Top}/Daredevil v2 ½-119+500-512 &Extras (1998-2009)/Daredevil Annual 01 (2007) (digital) (Minutemen-InnerDemons).cbr",
                $"{Top}/Daredevil v3 01-036+Annual (2011-2014)/Daredevil 001 (2011) (digital) (Minutemen-InnerDemons).cbr",
                $"{Top}/Mini-Series/Daredevil Noir (001-004) (2009)/Daredevil Noir 01 (2009) (digital) (Minutemen-PhD).cbr",
                $"{Top}/Mini-Series/Daredevil Noir (001-004) (2009)/Daredevil Noir 02 (2009) (digital) (Minutemen-PhD).cbr",
                $"{Top}/Mini-Series/Daredevil Noir (001-004) (2009)/Variant Covers/Daredevil Noir 01 (2009) (Variant Cover Only) [extracted] (Archangel-DCP).cbr"),
            EbookTypes)!;

        Assert.Equal(
            [
                ("Daredevil Noir 01", "Daredevil Noir", "1"), ("Daredevil Noir 02", "Daredevil Noir", "2"),
                ("Daredevil v1 001", "Daredevil v1", "1"), ("Daredevil v1 002", "Daredevil v1", "2"), ("Daredevil v1 051", "Daredevil v1", "51"),
                ("Daredevil v1 Annual 001", "Daredevil v1 Annual", "1"), ("Daredevil v2 001", "Daredevil v2", "1"),
                ("Daredevil v2 Annual 01", "Daredevil v2 Annual", "1"), ("Daredevil v3 001", "Daredevil v3", "1"),
            ],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series.FirstOrDefault()?.Name, b.Metadata.Series.FirstOrDefault()?.Entries[0].Display)).Order());
        Assert.Equal("cbz", books.Single(b => b.Metadata.Title == "Daredevil v1 Annual 001").Format); // one book in two formats
        Assert.Equal(2, books.Single(b => b.Metadata.Title == "Daredevil Noir 01").Files.Count); // with its variant cover
    }

    [Fact]
    public void YearsAfterAHyphen_AreNumbers_AndACollectionsTitleIsNoSeriesName()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Complete Doonesbury - G. B. Trudeau", Authors = ["G B Trudeau"] },
            Files(
                "Complete Doonesbury (1970-2013)/Doonesbury-1970.cbz",
                "Complete Doonesbury (1970-2013)/Doonesbury-1971.cbz",
                "Complete Doonesbury (1970-2013)/Doonesbury-1972.cbz",
                "Complete Doonesbury (1970-2013)/Extra/BullTales.cbz"),
            EbookTypes)!;

        Assert.Equal(
            [("BullTales", null, null), ("Doonesbury-1970", "Doonesbury", "1970"), ("Doonesbury-1971", "Doonesbury", "1971"), ("Doonesbury-1972", "Doonesbury", "1972")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series.FirstOrDefault()?.Name, b.Metadata.Series.FirstOrDefault()?.Entries[0].Display)));
    }

    [Fact]
    public void MagazineIssuesByMonthAndYear_AreNumberedByTheirDate()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.PeriodicalEbook) with { Title = "Popular Mechanics Magazine 1905-1995", Authors = ["Popular Mechanics"] },
            Files(
                "Popular Mechanics/1927/Popular_Mechanics_10_1927.pdf",
                "Popular Mechanics/1928/Popular_Mechanics_10_1928.pdf",
                "Popular Mechanics/1928/Popular_Mechanics_01_1928.pdf",
                "Popular Mechanics/1961/Popular Mechanics-07-1961.pdf"),
            EbookTypes)!;

        Assert.Equal(
            [("Popular Mechanics 10 1927", "1927.10"), ("Popular Mechanics 01 1928", "1928.01"), ("Popular Mechanics 10 1928", "1928.10"), ("Popular Mechanics-07-1961", "1961.07")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Entries[0].Display)).OrderBy(b => b.Item2, StringComparer.Ordinal));
        Assert.All(books, b => Assert.Equal("Popular Mechanics", b.Metadata.Series[0].Name));
    }

    [Fact]
    public void ABookTheTorrentHoldsTwice_IsOneBook_TheBestCopyKept()
    {
        const string Top = "The Dark Tower";
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "The Dark Tower Collection (2007-2016)", Authors = ["Stephen King"] },
            [
                new($"{Top}/The Dark Tower - The Drawing of Three - The Prisoner (01-05)/The Dark Tower - The Drawing of the Three - The Prisoner 01 (of 05) (digital).cbr", 100),
                new($"{Top}/The Dark Tower - The Drawing of the Three - The Prisoner (001-005)/The Dark Tower - The Drawing of the Three - The Prisoner 01 (of 05) (digital).cbr", 200),
                new($"{Top}/The Dark Tower - The Drawing of the Three - The Prisoner (001-005)/The Dark Tower - The Drawing of the Three - The Prisoner 02 (of 05) (2014) (digital).cbr", 100),
                new($"{Top}/The Dark Tower - The Drawing of Three - The Prisoner (01-05)/The Dark Tower - The Drawing of the Three - The Prisoner 02 (of 05) (2014) (digital).cbz", 100),
                new($"{Top}/The Dark Tower - The Drawing of the Three - The Prisoner (001-005)/The Dark Tower - The Drawing of the Three - The Prisoner 03 (of 05) (2014) (digital).cbr", 100),
            ],
            EbookTypes)!;

        Assert.Equal(
            [(1, "The Dark Tower - The Drawing of the Three - The Prisoner 01", 200L, "cbr"), (2, "The Dark Tower - The Drawing of the Three - The Prisoner 02", 100L, "cbz"), (3, "The Dark Tower - The Drawing of the Three - The Prisoner 03", 100L, "cbr")],
            books.Select(b => (b.Position, b.Metadata.Title, b.Metadata.TotalSizeBytes, b.Format)));
    }

    [Fact]
    public void ARangeAfterAHyphen_IsNoNumber()
    {
        Assert.Null(CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook),
            Files("C/Saga 1-6.cbr", "C/Saga 7-12.cbr", "C/Saga 13-18.cbr"),
            EbookTypes));
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
    public void AReadingOrder_LeavesItsPositionsOutOfTheNames_AndPutsStoryArcsInTheirSeries()
    {
        const string Top = "Nightfall Reading Order/Nightfall 001";
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Nightfall Reading Order 1990-2010", Authors = ["Ann Artist"] },
            Files(
                $"{Top}/001_Nightfall - Year Zero.cbr",
                $"{Top}/002_Lantern - First Light.cbr",
                $"{Top}/003_Nightfall - Long Night (Part 1).cbr",
                $"{Top}/004_Nightfall - Long Night (Part 2).cbr",
                $"{Top}/005 Nightfall 683.cbr",
                $"{Top}/006_Lantern - Last Light.cbr",
                $"{Top}/007_Villains United.cbr",
                $"{Top}/008_Nightfall_Poison Garden.cbr",
                $"{Top}/009_Nightfall_Broken Worlds - Hidden Door.cbr",
                $"{Top}/010_Lantern - The Long Winter/010_Lantern - The Long Winter.cbr",
                $"{Top}/010_Lantern - The Long Winter/010_Lantern - The Long Winter.cbz",
                $"{Top}/Lantern v2 #69.cbr"),
            EbookTypes)!;

        Assert.Equal(
            [
                ("Year Zero", "Nightfall", null), ("First Light", "Lantern", null), ("Long Night (Part 1)", "Nightfall", null),
                ("Long Night (Part 2)", "Nightfall", null), ("Nightfall 683", "Nightfall", "683"), ("Last Light", "Lantern", null),
                ("Villains United", null, null), ("Poison Garden", "Nightfall", null), ("Broken Worlds - Hidden Door", "Nightfall", null),
                ("The Long Winter", "Lantern", null), ("Lantern v2 #69", "Lantern v2", "69"),
            ],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series.FirstOrDefault()?.Name, b.Metadata.Series.FirstOrDefault()?.Entries.FirstOrDefault()?.Display)));
        Assert.Equal("cbz", books.Single(b => b.Metadata.Title == "The Long Winter").Format); // one book in two formats
    }

    [Fact]
    public void AReadingOrderOfNumberedIssues_IsACollection_OnceThePositionsAreLeftOut()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook) with { Title = "Comics Reading Order 1975" },
            Files(
                "Reading Order (1975)/01-Jan/04417 Hollow House 11.cbr",
                "Reading Order (1975)/01-Jan/04418 Star Rangers 5.cbr",
                "Reading Order (1975)/02-Feb/04450 Hollow House 12 (c2c).cbr",
                "Reading Order (1975)/03-March/04481 Hollow House 13.cbz"),
            EbookTypes)!;

        Assert.Equal(
            [("Hollow House 11", "Hollow House", "11"), ("Star Rangers 5", "Star Rangers", "5"), ("Hollow House 12", "Hollow House", "12"), ("Hollow House 13", "Hollow House", "13")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Series[0].Name, b.Metadata.Series[0].Entries[0].Display)));
    }

    [Fact]
    public void OneNumberStartingEveryName_IsNoReadingOrder()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook),
            Files("C/2000 Tales 001.cbr", "C/2000 Tales 002.cbr", "C/2000 Tales 003.cbr"),
            EbookTypes)!;

        Assert.All(books, b => Assert.Equal("2000 Tales", b.Metadata.Series[0].Name));
    }

    [Fact]
    public void AStoryArcWhoseSeriesNoOtherBookHas_HasNoSeries()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null, mediaType: MediaType.ComicBook),
            Files("C/001_Saga 1.cbr", "C/002_Saga 2.cbr", "C/003_Saga 3.cbr", "C/004_Lantern - First Light.cbr"),
            EbookTypes)!;

        Assert.Equal(("Lantern - First Light", null), books.Select(b => (b.Metadata.Title, b.Metadata.Series.FirstOrDefault()?.Name)).Last());
    }

    [Fact]
    public void ABundleOfUnrelatedBooks_IsSplit_EachBookWithItsOwnAuthor()
    {
        var books = CollectionSplitter.Split(
            Collection(seriesEntries: null) with { Title = "Illustrated Classics", Authors = ["Jane Austen", "Charles Dickens", "Charlotte Brontë", "George Eliot"] },
            Files(
                "Classics/Austen, Jane - Emma.pdf",
                "Classics/Austen, Jane - Persuasion.pdf",
                "Classics/Bronte, Charlotte - Jane Eyre.pdf",
                "Classics/Charles Dickens - Bleak House.pdf",
                "Classics/Eliot, George  - Middlemarch.pdf"),
            EbookTypes)!;

        Assert.Equal(
            [("Emma", "Jane Austen"), ("Persuasion", "Jane Austen"), ("Jane Eyre", "Charlotte Brontë"), ("Bleak House", "Charles Dickens"), ("Middlemarch", "George Eliot")],
            books.Select(b => (b.Metadata.Title, b.Metadata.Authors.Single())));
        Assert.All(books, b => Assert.Empty(b.Metadata.Series));
    }

    [Fact]
    public void OneAuthorsBooks_AreNoBundle()
    {
        Assert.Null(CollectionSplitter.Split(
            Collection(seriesEntries: null) with { Authors = ["Jane Austen"] },
            Files("D/Austen, Jane - Emma.epub", "D/Austen, Jane - Persuasion.epub", "D/Austen, Jane - Sanditon.epub"),
            EbookTypes));
    }

    [Fact]
    public void BooksByAuthorsTheTorrentDoesNotList_AreNoBundle()
    {
        Assert.Null(CollectionSplitter.Split(
            Collection(seriesEntries: null) with { Authors = ["Somebody Else"] },
            Files("D/Austen, Jane - Emma.epub", "D/Dickens, Charles - Bleak House.epub", "D/Eliot, George - Middlemarch.epub"),
            EbookTypes));
    }

    [Fact]
    public void OneBookNamedAfterItsAuthor_InTwoFormats_IsNoBundle()
    {
        Assert.Null(CollectionSplitter.Split(
            Collection(seriesEntries: null) with { Authors = ["John Smith"] },
            Files("D/Smith, John - Title.epub", "D/Smith, John - Title.mobi"),
            EbookTypes));
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
