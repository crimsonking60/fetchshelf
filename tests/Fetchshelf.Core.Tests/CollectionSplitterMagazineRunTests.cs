using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

/// <summary>Magazine runs (plan <c>magazine-runs.md</c>), and what <see cref="CollectionSplitter.Analyze"/> reports.</summary>
public class CollectionSplitterMagazineRunTests
{
    private static readonly IReadOnlyList<string> EbookTypes = ["epub", "azw3", "mobi", "pdf", "cbz", "cbr"];

    private static TorrentMetadata Run(string title, params Series[] series) => new()
    {
        MamId = 1001,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.PeriodicalEbook,
        LegacyCategory = LegacyCategory.EbookMagazinesNewspapers,
        Language = Language.English,
        Title = title,
        Authors = ["Various"],
        Series = series,
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    private static Series MamSeries(string name, string entries = "") => SeriesParser.Parse(name, entries);

    private static List<TorrentFileEntry> Files(params string[] names) => names.Select(n => new TorrentFileEntry($"Run/{n}", 100)).ToList();

    private static CollectionSplit Analyze(TorrentMetadata run, params string[] names) => CollectionSplitter.Analyze(run, Files(names), EbookTypes);

    /// <summary>Each book as title | series | number.</summary>
    private static List<string> Books(CollectionSplit split) => split.Books!
        .Select(b => $"{b.Metadata.Title} | {b.Metadata.Series.SingleOrDefault()?.Name} | {b.Metadata.Series.SingleOrDefault()?.Entries.SingleOrDefault()?.Display}")
        .ToList();

    [Fact]
    public void IssueNumbers_NumberTheRun_EvenWhenTheMonthsRepeat_AndADoubleIssueIsOneBookWithARange()
    {
        var split = Analyze(
            Run("Lantern Monthly: The First Years"),
            "Lantern Monthly 1967 Issue 1 (November 09).pdf",
            "Lantern Monthly 1967 Issue 2 (November 23).pdf",
            "Lantern Monthly 1967 Issue 3 (December 14).pdf",
            "Lantern Monthly 1968 Issue 4-5 (January 20).pdf");

        Assert.Equal((SplitStrategy.MagazineRun, MagazineNumbering.IssueNumber), (split.Strategy, split.Numbering));
        Assert.Equal("split as a magazine run, numbered by issue", split.Description);
        Assert.Equal(
            [
                "Lantern Monthly 1967 Issue 1 | Lantern Monthly | 1",
                "Lantern Monthly 1967 Issue 2 | Lantern Monthly | 2",
                "Lantern Monthly 1967 Issue 3 | Lantern Monthly | 3",
                "Lantern Monthly 1968 Issue 4-5 | Lantern Monthly | 4-5",
            ],
            Books(split));
        Assert.Equal(SeriesEntryKind.Range, split.Books![3].Metadata.Series[0].Entries[0].Kind);
        Assert.Empty(split.Warnings);
    }

    [Fact]
    public void YearAndMonth_NumberTheRun_WithMamsSeriesName_AndTheSeriesInFrontOfANameWithoutIt()
    {
        var split = Analyze(
            Run("Nightfall Stories - 2019 Issues", MamSeries("Nightfall Stories Magazine")),
            "2019 01-02 (Nightfall Stories Book 136).epub",
            "2019 03-04 (Nightfall Stories Book 136).epub",
            "2019 05-06 (Nightfall Stories Book 136).epub");

        Assert.Equal(MagazineNumbering.YearAndMonth, split.Numbering);
        Assert.Equal(
            [
                "Nightfall Stories Magazine 2019 01-02 | Nightfall Stories Magazine | 2019.01-2019.02",
                "Nightfall Stories Magazine 2019 03-04 | Nightfall Stories Magazine | 2019.03-2019.04",
                "Nightfall Stories Magazine 2019 05-06 | Nightfall Stories Magazine | 2019.05-2019.06",
            ],
            Books(split));
    }

    [Fact]
    public void VolumeAndNumber_NumberTheRun_WhenAMonthHasTwoIssues()
    {
        var split = Analyze(
            Run("Nightfall Stories 1980-1982"),
            "Nightfall Stories v04n12 (Nov 1980).cbz",
            "Nightfall Stories v04n13 (Dec 1980).cbz",
            "Nightfall Stories v04n14 (Dec 1980).cbz",
            "Nightfall Stories v05n01 (Jan 1981).cbz");

        Assert.Equal(MagazineNumbering.VolumeAndNumber, split.Numbering);
        Assert.Equal(["4.12", "4.13", "4.14", "5.01"], split.Books!.Select(b => b.Metadata.Series[0].Entries[0].Display));
        Assert.Empty(split.Warnings);
    }

    [Fact]
    public void ARenamedMagazine_IsOneSeries_MamsName_WithoutAWarning()
    {
        var split = Analyze(
            Run("Lantern Science Fiction 1986-1993", MamSeries("Lantern Science Fiction Magazine")),
            "Lantern Science Fiction Magazine v14n11 1990-11.cbz",
            "Lantern Science Fiction Magazine v14n12 1990-12.cbz",
            "Lantern Science Fiction v17n01 1993-01.cbz",
            "Lantern Science Fiction v17n02 1993-02.cbz");

        Assert.All(split.Books!, b => Assert.Equal("Lantern Science Fiction Magazine", b.Metadata.Series[0].Name));
        Assert.Equal("Lantern Science Fiction v17n01 1993-01", split.Books![2].Metadata.Title); // each keeps its own name
        Assert.Empty(split.Warnings);
    }

    [Fact]
    public void ARenamedMagazine_WithoutMamsSeries_TakesTheMostCommonName_AndWarns()
    {
        var split = Analyze(
            Run("Hollow House Review 1981-1995"),
            "Hollow House Quarterly No 1 May 1981.pdf",
            "Hollow House Review No 84 May 1995.pdf",
            "Hollow House Review No 85 July 1995.pdf");

        Assert.All(split.Books!, b => Assert.Equal("Hollow House Review", b.Metadata.Series[0].Name));
        var warning = Assert.Single(split.Warnings);
        Assert.Equal(SplitWarningKind.SeveralMagazineNames, warning.Kind);
        Assert.Equal(["Hollow House Review", "Hollow House Quarterly"], warning.Examples);
    }

    [Fact]
    public void NamesWithoutTheMagazine_AndNoMamSeries_TakeTheTorrentsTitleWithoutItsYears()
    {
        var split = Analyze(
            Run("Nightfall Stories - All Issues 1953-1954"),
            "1953, 10 (October).pdf",
            "1953, 11 (November).pdf",
            "1953, 12 (December).pdf");

        Assert.Equal(
            ["Nightfall Stories 1953, 10 | Nightfall Stories | 1953.10", "Nightfall Stories 1953, 11 | Nightfall Stories | 1953.11", "Nightfall Stories 1953, 12 | Nightfall Stories | 1953.12"],
            Books(split));
    }

    [Fact]
    public void TheSameIssueUnderTwoNames_IsOneBook()
    {
        var split = Analyze(
            Run("Nightfall Stories 1990-1991"),
            "Nightfall Stories v79n04 1990-10.cbz",
            "Nightfall Stories v79n04 (1990-10).cbr",
            "Nightfall Stories v79n05 1990-11.cbz",
            "Nightfall Stories v79n06 1990-12.cbz");

        Assert.Equal(["1990.10", "1990.11", "1990.12"], split.Books!.Select(b => b.Metadata.Series[0].Entries[0].Display));
        Assert.Equal("cbz", split.Books![0].Format);
    }

    [Fact]
    public void ASingleIssueInSeveralFiles_IsNotARun()
    {
        var split = Analyze(
            Run("Lantern #5"),
            "Lantern Issue 5 Cover.pdf",
            "Lantern Issue 5 Pages.pdf",
            "Lantern Issue 5 Spreads.pdf",
            "Lantern Issue 5 Postcard.pdf");

        Assert.Null(split.Books);
        Assert.Equal("no series range, and no numbering shared by enough books", split.NotSplitReason);
    }

    [Fact]
    public void AnIssueOfStories_IsNotSplit()
    {
        var split = Analyze(Run("Nightfall Stories, March 2025"), "01 Introduction.epub", "The Long Winter.epub", "Hollow House.epub");

        Assert.Null(split.Books);
    }

    [Fact]
    public void ARangeCollection_OfVolumeAndNumberNames_IsARun()
    {
        var split = Analyze(
            Run("Nightfall Stories 1986", MamSeries("Nightfall Stories", "1-12")),
            "Nightfall Stories v10n01 1986-01.cbz",
            "Nightfall Stories v10n02 1986-02.cbz",
            "Nightfall Stories v10n03 1986-03.cbz");

        Assert.Equal((SplitStrategy.MagazineRun, MagazineNumbering.YearAndMonth), (split.Strategy, split.Numbering));
        Assert.Equal(["1986.01", "1986.02", "1986.03"], split.Books!.Select(b => b.Metadata.Series[0].Entries[0].Display));
    }

    [Fact]
    public void NoNumberingForEveryIssue_SplitsWithoutNumbers_AndWarns()
    {
        var split = Analyze(
            Run("Lantern Odds and Ends"),
            "Lantern v01n01.pdf",
            "Lantern 1990-02.pdf",
            "Lantern No 5.pdf");

        Assert.Equal((SplitStrategy.MagazineRun, MagazineNumbering.None), (split.Strategy, split.Numbering));
        Assert.All(split.Books!, b => Assert.Empty(b.Metadata.Series[0].Entries));
        var warning = Assert.Single(split.Warnings, w => w.Kind == SplitWarningKind.NumberingNotRecognized);
        Assert.Contains("2 issues have no issue number", warning.Message);
    }

    [Fact]
    public void SomeIssuesWithAnIssueNumber_AndOthersWithout_Warns_AndTheYearAndMonthNumberThem()
    {
        var split = Analyze(
            Run("Hollow House Review 1981"),
            "Hollow House Review No 1 May 1981.pdf",
            "Hollow House Review No 2 July 1981.pdf",
            "Hollow House Review September 1981.pdf",
            "Hollow House Review November 1981.pdf");

        Assert.Equal(MagazineNumbering.YearAndMonth, split.Numbering);
        var warning = Assert.Single(split.Warnings);
        Assert.Equal((SplitWarningKind.InconsistentNumbering, "2 of 4 issues have an issue number, the others don't"), (warning.Kind, warning.Message));
        Assert.Equal(["Hollow House Review November 1981", "Hollow House Review September 1981"], warning.Examples);
    }

    [Fact]
    public void ABookThatIsNotAnIssue_IsInTheSeriesWithoutANumber_AndWarns()
    {
        var split = Analyze(
            Run("Lantern 2020"),
            "Lantern Issue 34 2020.pdf",
            "Lantern Issue 35 2020.pdf",
            "Lantern Issue 36 2020.pdf",
            "Lantern Issue 37 2020.pdf",
            "Lantern Yearly Index.pdf");

        var index = split.Books!.Single(b => b.Metadata.Title == "Lantern Yearly Index");
        Assert.Equal("Lantern", index.Metadata.Series[0].Name);
        Assert.Empty(index.Metadata.Series[0].Entries);
        var warning = Assert.Single(split.Warnings);
        Assert.Equal(("1 of 5 books don't read as issues and have no number", "Lantern Yearly Index"), (warning.Message, warning.Examples.Single()));
    }

    [Fact]
    public void OtherCollections_SayHowTheyWereRead()
    {
        var range = Analyze(Run("Nightfall", MamSeries("Nightfall", "1-3")), "01 - Year Zero.epub", "02 - The Long Winter.epub");
        var numbered = Analyze(Run("Star Rangers"), "Star Rangers 001.cbz", "Star Rangers 002.cbz", "Star Rangers 003.cbz");
        var single = Analyze(Run("Hollow House"), "Hollow House.epub");

        Assert.Equal(("split by MAM's series range", "split as a series numbered in its file names"), (range.Description, numbered.Description));
        Assert.Equal(CollectionSplit.Nothing, single);
    }

    [Fact]
    public void ARangeCollection_WithABookWithoutANumber_Warns()
    {
        var split = Analyze(
            Run("Nightfall", MamSeries("Nightfall", "1-4")),
            "01 - Year Zero.epub", "02 - The Long Winter.epub", "03 - Hollow House.epub", "Lantern Tales.epub");

        var warning = Assert.Single(split.Warnings);
        Assert.Equal((SplitWarningKind.BooksWithoutNumber, "Lantern Tales"), (warning.Kind, warning.Examples.Single()));
    }
}
