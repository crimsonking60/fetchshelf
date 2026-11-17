using System.Text.RegularExpressions;
using Fetchshelf.Web.Pages.Account;

namespace Fetchshelf.Web.Tests;

public sealed partial class SparklineSvgTests
{
    private static List<string> Polylines(string svg) => PolylinePattern().Matches(svg).Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public void Build_ScalesTheLowestToTheBottomAndTheHighestToTheTop()
    {
        var svg = SparklineSvg.Build([1, 3, 2], "Ratio");

        Assert.Equal(["3,57 150,3 297,30"], Polylines(svg));
        Assert.Contains("role=\"img\" aria-label=\"Ratio\"", svg);
    }

    [Fact]
    public void Build_DaysWithoutAValue_AreGaps_AndALoneValueIsADot()
    {
        var svg = SparklineSvg.Build([1, 2, null, 3, null, 4, 5], "Ratio");

        Assert.Equal(2, Polylines(svg).Count);
        Assert.Single(CirclePattern().Matches(svg));
    }

    [Fact]
    public void Build_OneValueOnly_IsADotInTheMiddle() =>
        Assert.Contains("<circle cx=\"150\" cy=\"30\"", SparklineSvg.Build([null, 7.0, null], "Wedges"));

    [Fact]
    public void Build_ValuesCloserThanTheMinimumSpan_StayNearTheMiddle()
    {
        // 2.38 to 2.39 is under 2% of 2.39: drawn within the middle half of the chart, not from bottom to top.
        var points = Polylines(SparklineSvg.Build([2.38, 2.39], "Ratio"))[0].Split(' ').Select(p => double.Parse(p.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.All(points, y => Assert.InRange(y, 15, 45));
        Assert.True(points[0] > points[1]);
    }

    [Fact]
    public void Build_TheSameValueEveryDay_IsAFlatLineAcrossTheMiddle() =>
        Assert.Equal(["3,30 297,30"], Polylines(SparklineSvg.Build([4, 4], "Wedges")));

    [Fact]
    public void Build_NoValues_IsAnEmptyChart_AndValuesThatArentFiniteAreGaps()
    {
        Assert.DoesNotContain("<polyline", SparklineSvg.Build([null, null], "Ratio"));
        Assert.Equal(2, Polylines(SparklineSvg.Build([1, 2, double.PositiveInfinity, 3, 4], "Ratio")).Count);
    }

    [Fact]
    public void Build_TheDescription_IsEncoded() =>
        Assert.Contains("aria-label=\"a &lt;b&gt; &amp; &quot;c&quot;\"", SparklineSvg.Build([1.0], "a <b> & \"c\""));

    [GeneratedRegex("<polyline points=\"([^\"]+)\"")]
    private static partial Regex PolylinePattern();

    [GeneratedRegex("<circle ")]
    private static partial Regex CirclePattern();
}
