using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Tests;

public class SortStateTests
{
    [Fact]
    public void Toggle_AColumnNotSortedBy_StartsAscending_ByDefault() =>
        Assert.Equal(new SortState("Title", false), new SortState("Added", true).Toggle("Title"));

    [Fact]
    public void Toggle_ANumberOrDateColumn_StartsDescending() =>
        Assert.Equal(new SortState("Added", true), new SortState("Title", false).Toggle("Added", firstDescending: true));

    [Fact]
    public void Toggle_TheColumnSortedBy_FlipsTheDirection_NeverRemovingTheSort()
    {
        var state = new SortState("Title", false);

        var once = state.Toggle("Title");
        var twice = once.Toggle("Title");

        Assert.Equal(new SortState("Title", true), once);
        Assert.Equal(state, twice);
    }

    [Theory]
    [InlineData("desc", true)]
    [InlineData("DESC", true)]
    [InlineData("asc", false)]
    [InlineData(null, false)]
    [InlineData("sideways", false)]
    public void From_ReadsTheDirectionAsTheQueryStringCarriesIt(string? direction, bool descending) =>
        Assert.Equal(descending, SortState.From("Title", direction).Descending);

    [Fact]
    public void AriaSort_SaysWhichColumnIsSortedAndHow()
    {
        var state = new SortState("Title", true);

        Assert.Equal("descending", state.AriaSort("Title"));
        Assert.Equal("none", state.AriaSort("Added"));
        Assert.Equal("ascending", state.Toggle("Title").AriaSort("Title"));
    }
}
