using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// The Torrents page's Filters panel opens while a filter is in use, and otherwise as it was left: remember-open.js keeps
/// that in a cookie, which the page reads so it renders open with no flash. The search box and its button share one line.
/// </summary>
public partial class TorrentFilterPanelTests
{
    [GeneratedRegex(@"<details class=""filter-panel""[^>]*>")]
    private static partial Regex FilterPanelPattern();

    private static async Task<string> FilterPanelAsync(string url, string? cookie = null)
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return FilterPanelPattern().Match(await response.Content.ReadAsStringAsync()).Value;
    }

    [Fact]
    public async Task Get_NoFilterAndNothingRemembered_StartsClosed()
    {
        var panel = await FilterPanelAsync("/torrents");

        Assert.DoesNotContain("open", panel.Replace("data-remember-open", "", StringComparison.Ordinal));
        Assert.Contains("data-remember-open=\"fs-torrent-filters\"", panel);
    }

    [Fact]
    public async Task Get_LeftOpenOnAnEarlierVisit_StartsOpen()
    {
        var panel = await FilterPanelAsync("/torrents", "fs-torrent-filters=open");

        Assert.Contains(" open=\"open\"", panel);
    }

    [Fact]
    public async Task Get_LeftClosed_StartsClosed()
    {
        var panel = await FilterPanelAsync("/torrents", "fs-torrent-filters=closed");

        Assert.DoesNotContain(" open=\"open\"", panel);
    }

    [Fact]
    public async Task Get_AFilterInUse_StartsOpen_EvenWhenLeftClosed()
    {
        var panel = await FilterPanelAsync("/torrents?Status=linked", "fs-torrent-filters=closed");

        Assert.Contains(" open=\"open\"", panel);
    }

    [Fact]
    public async Task Get_Torrents_KeepsTheSearchBoxAndButtonOnOneLine_AndLoadsTheScript()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.Matches(@"<div class=""toolbar search-line"">\s*<input type=""text"" name=""Q""", html);
        Assert.Contains("/js/remember-open.", html); // fingerprinted file name
        var css = await client.GetStringAsync(Regex.Match(html, "href=\"(/css/site[^\"]*)\"").Groups[1].Value);
        Assert.Contains(".toolbar.search-line { flex-wrap: nowrap; }", css);
    }

    [Fact]
    public async Task Script_RecordsOnlyAClickOnTheSummary_AsTheStateItTurnsTo()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/torrents");

        var script = await client.GetStringAsync(Regex.Match(page, "src=\"(/js/remember-open[^\"]*)\"").Groups[1].Value);

        Assert.Contains("addEventListener('click'", script);
        Assert.DoesNotContain("addEventListener('toggle'", script);
        Assert.Contains("details.open ? 'closed' : 'open'", script);
    }
}
