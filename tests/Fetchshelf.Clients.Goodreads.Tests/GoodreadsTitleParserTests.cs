namespace Fetchshelf.Clients.Goodreads.Tests;

public class GoodreadsTitleParserTests
{
    [Fact]
    public void ExtractSeries_SuffixPresent_SplitsTitleAndSeries()
    {
        var (title, seriesName, seriesNumber) = GoodreadsTitleParser.ExtractSeries("Mistborn (The Mistborn Saga, #1)");

        Assert.Equal("Mistborn", title);
        Assert.Equal("The Mistborn Saga", seriesName);
        Assert.Equal(1, seriesNumber);
    }

    [Fact]
    public void ExtractSeries_NoSuffix_ReturnsTitleUnchangedWithNullSeries()
    {
        var (title, seriesName, seriesNumber) = GoodreadsTitleParser.ExtractSeries("Good Omens");

        Assert.Equal("Good Omens", title);
        Assert.Null(seriesName);
        Assert.Null(seriesNumber);
    }

    [Fact]
    public void ExtractSeries_FractionalNumber_Parses()
    {
        var (_, _, seriesNumber) = GoodreadsTitleParser.ExtractSeries("Novella (A Series, #1.5)");

        Assert.Equal(1.5, seriesNumber);
    }

    [Fact]
    public void Unescape_HtmlEntities_Decoded()
    {
        var result = GoodreadsTitleParser.Unescape("Rock &amp; Roll");

        Assert.Equal("Rock & Roll", result);
    }
}
