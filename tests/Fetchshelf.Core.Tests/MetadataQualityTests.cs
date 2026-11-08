using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class MetadataQualityTests
{
    private static readonly string LongDescription = new('x', MetadataQuality.MinDescriptionLength);

    private static MetadataQualityFacts Complete(bool isAudio = false) => new(
        HasAuthor: true, Description: LongDescription, HasCover: true, HasGenres: true, IsAudio: isAudio, HasNarrator: true, HasIdentifier: true,
        HasFileTypes: true);

    [Fact]
    public void Rate_EverythingThere_IsGoodWithNothingMissing()
    {
        var rating = MetadataQuality.Rate(Complete(isAudio: true));

        Assert.Equal(MetadataQualityGrade.Good, rating.Grade);
        Assert.Empty(rating.Missing);
    }

    [Fact]
    public void Rate_OneSmallGap_IsStillGood()
    {
        var rating = MetadataQuality.Rate(Complete() with { HasIdentifier = false });

        Assert.Equal(MetadataQualityGrade.Good, rating.Grade);
        Assert.Equal(["ISBN or ASIN"], rating.Missing);
    }

    [Fact]
    public void Rate_NoDescription_IsLacking()
    {
        var rating = MetadataQuality.Rate(Complete() with { Description = null });

        Assert.Equal(MetadataQualityGrade.Lacking, rating.Grade);
        Assert.Equal(["description"], rating.Missing);
    }

    [Fact]
    public void Rate_TwoSmallGaps_IsLacking() =>
        Assert.Equal(MetadataQualityGrade.Lacking, MetadataQuality.Rate(Complete() with { HasCover = false, HasGenres = false }).Grade);

    [Fact]
    public void Rate_ThreePointsMissing_IsPoor()
    {
        var rating = MetadataQuality.Rate(Complete() with { Description = "", HasCover = false });

        Assert.Equal(MetadataQualityGrade.Poor, rating.Grade);
        Assert.Equal(["description", "cover"], rating.Missing);
    }

    [Fact]
    public void Rate_NoAuthor_IsPoorWhateverElseItHas()
    {
        var rating = MetadataQuality.Rate(Complete() with { HasAuthor = false });

        Assert.Equal(MetadataQualityGrade.Poor, rating.Grade);
        Assert.Equal(["author"], rating.Missing);
    }

    [Fact]
    public void Rate_NoFileTypes_IsPoorWhateverElseItHas()
    {
        var rating = MetadataQuality.Rate(Complete() with { HasFileTypes = false });

        Assert.Equal(MetadataQualityGrade.Poor, rating.Grade);
        Assert.Equal(["file types (Fetchshelf can't download it without them)"], rating.Missing);
    }

    [Fact]
    public void Rate_NarratorOnlyCountsForAudio()
    {
        Assert.Equal(MetadataQualityGrade.Good, MetadataQuality.Rate(Complete(isAudio: false) with { HasNarrator = false }).Grade);
        Assert.Equal(["narrator"], MetadataQuality.Rate(Complete(isAudio: true) with { HasNarrator = false }).Missing);
    }

    [Fact]
    public void Rate_VeryShortDescription_CountsAsMissing()
    {
        var rating = MetadataQuality.Rate(Complete() with { Description = "<p>[b]Great book![/b]</p>" });

        Assert.Equal(MetadataQualityGrade.Lacking, rating.Grade);
        Assert.Equal(["a real description (it is very short)"], rating.Missing);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("   ", 0)]
    [InlineData("<p>Hello <b>world</b></p>", 11)]
    [InlineData("[b]Hello[/b] [url=http://x]world[/url]", 11)]
    [InlineData("Fish &amp; chips\n\n  and   more", 21)]
    [InlineData("A [1] B", 7)]
    public void TextLength_LeavesOutMarkupAndExtraWhitespace(string? description, int length) =>
        Assert.Equal(length, MetadataQuality.TextLength(description));
}
