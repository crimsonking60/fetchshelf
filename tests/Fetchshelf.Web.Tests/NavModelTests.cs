using Fetchshelf.Web.Navigation;

namespace Fetchshelf.Web.Tests;

public class NavModelTests
{
    private static NavInputs Inputs(
        bool account = false, bool follow = false, bool lists = false, int errors = 0, int followCount = 0, int findings = 0) =>
        new(account, follow, lists, errors, errors == 0 ? "" : $"{errors} new errors", followCount, findings);

    private static string[] Labels(IEnumerable<NavEntry> entries) => entries.Select(e => e.Label).ToArray();

    [Fact]
    public void Build_NothingOptionalConfigured_HasTheDailyPagesOnTheBarAndTheRestInTwoMenus()
    {
        var model = NavModel.Build(Inputs());

        Assert.Equal(["Search", "Torrents", "Selected", "Jobs", "Errors"], Labels(model.Primary));
        Assert.Equal(["Library", "Insight"], model.Groups.Select(g => g.Label).ToArray());
        Assert.Equal(["Books", "Series", "Duplicates", "Library check"], Labels(model.Groups[0].Entries));
        Assert.Equal(["Events", "Statistics"], Labels(model.Groups[1].Entries));
        Assert.Equal(["Config"], Labels(model.Trailing));
    }

    [Fact]
    public void Build_Everything_AddsTheConfiguredEntriesInTheirPlaces()
    {
        var model = NavModel.Build(Inputs(account: true, follow: true, lists: true, findings: 4));

        Assert.Equal(["Books", "Series", "Following", "Lists", "Duplicates", "Library check"], Labels(model.Groups[0].Entries));
        Assert.Equal(["Account", "Config"], Labels(model.Trailing));
    }

    [Theory]
    [InlineData(false, false, 0, new[] { "Books", "Series", "Duplicates", "Library check" })]
    [InlineData(true, false, 0, new[] { "Books", "Series", "Following", "Duplicates", "Library check" })]
    [InlineData(false, true, 0, new[] { "Books", "Series", "Lists", "Duplicates", "Library check" })]
    [InlineData(false, false, 2, new[] { "Books", "Series", "Duplicates", "Library check" })]
    public void Build_LibraryMenu_FollowsTheConfiguration(bool follow, bool lists, int findings, string[] expected) =>
        Assert.Equal(expected, Labels(NavModel.Build(Inputs(follow: follow, lists: lists, findings: findings)).Groups[0].Entries));

    [Fact]
    public void Build_BadgesAppearOnlyWithSomethingToCount()
    {
        var quiet = NavModel.Build(Inputs(follow: true));
        var busy = NavModel.Build(Inputs(follow: true, errors: 3, followCount: 2, findings: 5));

        Assert.All(quiet.Primary.Concat(quiet.Groups.SelectMany(g => g.Entries)), e => Assert.Null(e.Badge));
        Assert.Equal(3, busy.Primary.Single(e => e.Label == "Errors").Badge!.Count);
        Assert.Equal("error-badge", busy.Primary.Single(e => e.Label == "Errors").Badge!.Id);
        Assert.Equal("2 finds to look at", busy.Groups[0].Entries.Single(e => e.Label == "Following").Badge!.Title);
    }

    [Fact]
    public void Build_AMenuButtonCarriesTheSumOfItsEntriesBadges()
    {
        var model = NavModel.Build(Inputs(follow: true, followCount: 2, findings: 5));

        Assert.Equal(7, model.Groups[0].BadgeTotal);
        Assert.Equal(0, model.Groups[1].BadgeTotal);
    }

    [Theory]
    [InlineData("/Torrents/Detail", "/Torrents/Index", true)]
    [InlineData("/Torrents/Index", "/Torrents/Index", true)]
    [InlineData("/Index", "/Index", true)]
    [InlineData("/Search/Detail", "/Index", false)]
    [InlineData("/Series/Index", "/Torrents/Index", false)]
    public void IsCurrent_AnEntryOwnsItsFolder_AndSearchOnlyItself(string current, string entry, bool expected) =>
        Assert.Equal(expected, NavModel.IsCurrent(current, entry));
}
