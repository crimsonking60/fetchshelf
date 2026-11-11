using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class SeriesGapsTests
{
    [Fact]
    public void Missing_PlainGap() =>
        Assert.Equal([3], SeriesGaps.Missing([(1, 1), (2, 2), (4, 4)]));

    [Fact]
    public void Missing_RangesCoverEveryNumberInThem() =>
        Assert.Equal([5], SeriesGaps.Missing([(1, 1), (2, 2), (3, 4), (6, 6)]));

    [Fact]
    public void Missing_APartCountsAsItsBook() =>
        Assert.Equal([1], SeriesGaps.Missing([(2, 2), (3, 3)]));

    [Fact]
    public void Missing_FractionsAreNeverMissing_ButARangeFromOneStillCovers()
    {
        Assert.Equal([2], SeriesGaps.Missing([(1, 1), (1.5, 1.5), (3, 3)]));
        Assert.Equal([1], SeriesGaps.Missing([(1.5, 3)]));
    }

    [Fact]
    public void Missing_ASeriesStartingAboveOne_MissesTheNumbersBelow() =>
        Assert.Equal([1, 2], SeriesGaps.Missing([(3, 3)]));

    [Fact]
    public void Missing_NothingMissing_OrNoWholeNumbers()
    {
        Assert.Empty(SeriesGaps.Missing([(1, 1), (2, 3)]));
        Assert.Empty(SeriesGaps.Missing([(0.5, 0.5)]));
        Assert.Empty(SeriesGaps.Missing([]));
    }

    [Fact]
    public void Missing_ASeriesNumberedByYear_IsNotARunOfBooks() =>
        Assert.Empty(SeriesGaps.Missing([(2019, 2019), (2021, 2021)]));

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 6 }, "1–4, 6")]
    [InlineData(new[] { 1, 2 }, "1, 2")]
    [InlineData(new[] { 5, 3, 1, 2 }, "1–3, 5")]
    [InlineData(new int[0], "")]
    public void Describe_RunsOfThreeOrMoreAsRanges(int[] numbers, string expected) =>
        Assert.Equal(expected, SeriesGaps.Describe(numbers));
}
