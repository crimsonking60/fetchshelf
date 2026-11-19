using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

public class MainNavPageTests
{
    private static string Nav(string html)
    {
        var start = html.IndexOf("<nav class=\"site-nav\"", StringComparison.Ordinal);
        return html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task EveryPage_HasTheDailyPagesOnTheBar_AndTheRestInMenus()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var nav = Nav(await fixture.CreateClient().GetStringAsync("/Jobs"));

        var firstMenu = nav.IndexOf("<details class=\"nav-menu\"", StringComparison.Ordinal);
        var bar = nav[..firstMenu];
        foreach (var label in new[] { "Search", "Torrents", "Selected", "Jobs", "Errors" })
        {
            Assert.Matches($"<a [^>]*>{label}", bar);
        }

        var library = Regex.Match(nav, "<details class=\"nav-menu[^\"]*\">\\s*<summary>Library.*?</details>", RegexOptions.Singleline).Value;
        Assert.Contains(">Series</a>", library);
        Assert.Contains(">Duplicates</a>", library);
        Assert.DoesNotContain(">Following", library);
        Assert.Matches("<details class=\"nav-menu[^\"]*\">\\s*<summary>Insight", nav);
        var config = nav.IndexOf(">Config</a>", StringComparison.Ordinal);
        Assert.True(config > nav.IndexOf("<summary>Insight", StringComparison.Ordinal) && config < nav.IndexOf("theme-menu", StringComparison.Ordinal), "Config comes after the menus, then the theme menu");
    }

    [Fact]
    public async Task APageInAMenu_LightsItsEntryAndTheMenuButton()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var nav = Nav(await fixture.CreateClient().GetStringAsync("/Statistics"));

        Assert.Matches("<a [^>]*aria-current=\"page\"[^>]*>Statistics</a>", nav);
        Assert.Matches("<details class=\"nav-menu current\">\\s*<summary>Insight", nav);
        Assert.DoesNotMatch("<details class=\"nav-menu current\">\\s*<summary>Library", nav);
    }

    [Fact]
    public async Task ABarPage_LightsNoMenu()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var nav = Nav(await fixture.CreateClient().GetStringAsync("/Torrents"));

        Assert.Matches("<a [^>]*aria-current=\"page\"[^>]*>Torrents</a>", nav);
        Assert.DoesNotContain("nav-menu current", nav);
    }

    [Fact]
    public async Task WithAFollowSection_FollowingIsInTheLibraryMenu()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [follow]
            category = "audiobooks"

            [[follow.grab]]
            cost = "free"
            """);

        var nav = Nav(await fixture.CreateClient().GetStringAsync("/Jobs"));

        var library = Regex.Match(nav, "<details class=\"nav-menu[^\"]*\">\\s*<summary>Library.*?</details>", RegexOptions.Singleline).Value;
        Assert.Contains(">Following</a>", library);
    }
}
