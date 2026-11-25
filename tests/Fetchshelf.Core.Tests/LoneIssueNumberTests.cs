using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LoneIssueNumberTests
{
    [Theory]
    [InlineData(new string[0], "1981.04")] // as a run: the year and month before the volume and number
    [InlineData(new[] { "Nightfall Stories #005.03 - Nightfall Stories v05n03.cbz", "series.json" }, "5.04")]
    [InlineData(new[] { "Nightfall Stories #1981.03 - March.cbz" }, "1981.04")]
    [InlineData(new[] { "Nightfall Stories #037 - March.cbz" }, "1981.04")] // the issue's name gives no issue number
    public void TheIssue_IsNumberedAsTheFoldersNumberedFilesMostlyAre(string[] siblings, string expected) =>
        Assert.Equal(expected, LoneIssueNumber.For(["Nightfall Stories v05n04 (#38) (1981-04) (Tagger)", "Nightfall Stories April 1981"], siblings)!.Display);

    [Fact]
    public void WithoutAnIssueInTheFileName_TheTorrentsTitleIsRead() =>
        Assert.Equal("1981.04", LoneIssueNumber.For(["nightfall-0481", "Nightfall Stories April 1981"], [])!.Display);

    [Fact]
    public void ANameThatIsNoIssue_GivesNoNumber() =>
        Assert.Null(LoneIssueNumber.For(["The Long Winter (Digital)", "The Long Winter"], []));

    [Theory]
    [InlineData("Nightfall Stories #005.04 - April 1981.cbr", "Nightfall Stories - April 1981.cbr", true)]
    [InlineData("Nightfall Stories #281-282 - Double.cbr", "Nightfall Stories - Double.cbr", true)]
    [InlineData("Nightfall Stories - April 1981.cbr", "Nightfall Stories - May 1981.cbr", false)]
    [InlineData("Nightfall Stories #005.04 - April 1981.cbr", "Nightfall Stories - May 1981.cbr", false)]
    public void IsNumbered_TellsALoneIssuesNameFromAnother(string linked, string expected, bool numbered) =>
        Assert.Equal(numbered, LoneIssueNumber.IsNumbered(linked, expected));
}
