using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Tests;

public class PagerItemsTests
{
    private static string Shown(int current, int total) =>
        string.Join(" ", PagerItems.For(current, total).Select(i => i.IsGap ? "…" : i.Page == current ? $"[{i.Page}]" : i.Page.ToString()));

    [Theory]
    [InlineData(1, 1, "[1]")]
    [InlineData(1, 3, "[1] 2 3")]
    [InlineData(4, 7, "1 2 3 [4] 5 6 7")]
    [InlineData(1, 297, "[1] 2 3 4 5 … 297")]
    [InlineData(3, 297, "1 2 [3] 4 5 … 297")]
    [InlineData(4, 297, "1 2 3 [4] 5 6 … 297")]
    [InlineData(6, 297, "1 … 4 5 [6] 7 8 … 297")]
    [InlineData(150, 297, "1 … 148 149 [150] 151 152 … 297")]
    [InlineData(294, 297, "1 … 292 293 [294] 295 296 297")]
    [InlineData(297, 297, "1 … 293 294 295 296 [297]")]
    [InlineData(5, 9, "1 2 3 4 [5] 6 7 8 9")]
    public void For_ShowsTheEndsTheCurrentPageAndItsNeighbours(int current, int total, string expected) =>
        Assert.Equal(expected, Shown(current, total));

    [Fact]
    public void For_AGapThatWouldHideOnePage_ShowsThatPageInstead() =>
        Assert.Equal("1 2 3 4 [5] 6 7 … 11", Shown(5, 11));

    [Fact]
    public void For_APageNumberPastTheEnd_IsClampedIntoRange() =>
        Assert.Equal(PagerItems.For(297, 297), PagerItems.For(999, 297));

    [Fact]
    public void For_NoPages_HasNoItems() => Assert.Empty(PagerItems.For(1, 0));
}
