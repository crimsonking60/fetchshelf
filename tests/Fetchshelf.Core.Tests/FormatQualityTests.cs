using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class FormatQualityTests
{
    private static readonly string[] EbookTypes = ["epub", "mobi", "pdf", "cbz", "cbr"];

    private static FormatQualityRating Rate(string[] fileTypes, MediaType mediaType = MediaType.Ebook, string[]? list = null) =>
        FormatQuality.Rate(list ?? EbookTypes, fileTypes, mediaType, "ebook_types");

    [Fact]
    public void Rate_ThePreferredFormat_IsGoodWithNoIssue() =>
        Assert.Equal(new FormatQualityRating(MetadataQualityGrade.Good, null), Rate(["pdf", "epub"]));

    [Fact]
    public void Rate_OnlyOtherFormatsInTheList_IsLackingAndSaysWhichIsMissing()
    {
        var rating = Rate(["mobi", "pdf"]);

        Assert.Equal(MetadataQualityGrade.Lacking, rating.Grade);
        Assert.Equal("Format: no epub (preferred), it has mobi, pdf.", rating.Issue);
    }

    [Fact]
    public void Rate_NoneOfTheFormatsInTheList_IsPoor()
    {
        var rating = Rate(["txt", "doc"]);

        Assert.Equal(MetadataQualityGrade.Poor, rating.Grade);
        Assert.Equal("Format: none of its file types (txt, doc) is in your ebook_types setting (epub, mobi, pdf, cbz, cbr).", rating.Issue);
    }

    [Theory]
    [InlineData(MediaType.ComicBook)]
    [InlineData(MediaType.Manga)]
    public void Rate_AComic_PrefersTheFirstComicFormat(MediaType comic)
    {
        Assert.Equal(MetadataQualityGrade.Good, Rate(["cbz"], comic).Grade);
        Assert.Equal("Format: no cbz (preferred), it has cbr.", Rate(["cbr"], comic).Issue);
    }

    [Fact]
    public void Rate_AComicInAnotherAcceptedFormat_IsLackingNotPoor() =>
        Assert.Equal(MetadataQualityGrade.Lacking, Rate(["pdf"], MediaType.ComicBook).Grade);

    [Fact]
    public void Rate_AnEbook_PrefersTheFirstFormatThatIsNotAComicFormat()
    {
        var comicsFirst = new[] { "cbz", "epub", "pdf" };

        Assert.Equal(MetadataQualityGrade.Good, Rate(["epub"], MediaType.Ebook, comicsFirst).Grade);
        Assert.Equal("Format: no epub (preferred), it has cbz.", Rate(["cbz"], MediaType.PeriodicalEbook, comicsFirst).Issue);
    }

    [Fact]
    public void Rate_AListWithoutAFormatOfTheRightKind_PrefersItsFirst() =>
        Assert.Equal("Format: no epub (preferred), it has pdf.", Rate(["pdf"], MediaType.ComicBook, ["epub", "pdf"]).Issue);

    [Fact]
    public void Rate_OtherMediaTypes_PreferTheFirstInTheList() =>
        Assert.Equal("Format: no m4b (preferred), it has mp3.", FormatQuality.Rate(["m4b", "mp3"], ["mp3"], MediaType.Audiobook, "audio_types").Issue);

    [Fact]
    public void Rate_FormatsAreComparedIgnoringCase() =>
        Assert.Equal(MetadataQualityGrade.Good, Rate(["EPUB"]).Grade);

    [Fact]
    public void Rate_NoFileTypes_IsLeftToTheMetadataRating() =>
        Assert.Equal(new FormatQualityRating(MetadataQualityGrade.Good, null), Rate([]));

    [Theory]
    [InlineData(MetadataQualityGrade.Good, MetadataQualityGrade.Lacking, MetadataQualityGrade.Lacking)]
    [InlineData(MetadataQualityGrade.Poor, MetadataQualityGrade.Lacking, MetadataQualityGrade.Poor)]
    [InlineData(MetadataQualityGrade.Good, MetadataQualityGrade.Good, MetadataQualityGrade.Good)]
    [InlineData(MetadataQualityGrade.Lacking, MetadataQualityGrade.Poor, MetadataQualityGrade.Poor)]
    public void Worse_IsTheLowerOfTheTwoGrades(MetadataQualityGrade first, MetadataQualityGrade second, MetadataQualityGrade expected) =>
        Assert.Equal(expected, FormatQuality.Worse(first, second));
}
