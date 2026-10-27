using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>The Torrents page's "Check qBittorrent" button.</summary>
public partial class TorrentsCheckClientTests
{
    private const string ConfigWithQBittorrent = """
        mam_id = ""

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"
        """;

    private static async Task<HttpResponseMessage> PostCheckAsync(HttpClient client, string tokenSourceHtml, bool fromHtmx, IDictionary<string, string>? extra = null)
    {
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractAntiforgeryToken(tokenSourceHtml) };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            fields[key] = value;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/torrents?handler=CheckClient") { Content = new FormUrlEncodedContent(fields) };
        if (fromHtmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Get_Page_HasTheCheckButtonWiredToHtmx()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.Contains("Check qBittorrent", html);
        Assert.Contains("handler=CheckClient", html);
        Assert.Contains("hx-target=\"#torrent-list-body\"", html);
    }

    [Fact]
    public async Task Post_CheckClient_FromHtmx_UpdatesStatusesAndReturnsTheListWithASummary()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer("hash1"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("In Client"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "gone", OrganizerTestSupport.BuildMetadata("Not In Client"));
        using var client = fixture.CreateNoRedirectClient();
        var page = await client.GetStringAsync("/torrents");
        Assert.DoesNotContain("Seeding", page);

        var response = await PostCheckAsync(client, page, fromHtmx: true);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<html", html); // just the list, for htmx to swap in
        Assert.Contains("Checked 2 torrents in qBittorrent: 1 found, 1 not in client.", html);
        Assert.Contains("Seeding", html);
        Assert.Contains("not in client</td>", html);

        var reloaded = await client.GetStringAsync("/torrents");
        Assert.Contains("Seeding", reloaded); // stored, not just displayed
        Assert.DoesNotContain("id=\"check-message\"", reloaded); // the message is not left in TempData
    }

    [Fact]
    public async Task Post_CheckClient_FromHtmx_KeepsTheCurrentSearchFilter()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer("hash1"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("Project Hail Mary"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("The Way of Kings"));
        using var client = fixture.CreateNoRedirectClient();
        var page = await client.GetStringAsync("/torrents?Q=Hail");

        var html = await (await PostCheckAsync(client, page, fromHtmx: true, new Dictionary<string, string> { ["Q"] = "Hail" })).Content.ReadAsStringAsync();

        Assert.Contains("Project Hail Mary", html);
        Assert.DoesNotContain("The Way of Kings", html);
        Assert.Contains("Checked 2 torrents", html); // the check covers every tracked torrent, not only the visible ones
    }

    [Fact]
    public async Task Post_CheckClient_WithoutHtmx_RedirectsAndShowsTheSummaryOnce()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer("hash1"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using var client = fixture.CreateNoRedirectClient();
        var page = await client.GetStringAsync("/torrents");

        var response = await PostCheckAsync(client, page, fromHtmx: false);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var landing = await client.GetStringAsync(response.Headers.Location!.OriginalString);
        Assert.Contains("Checked 1 torrent in qBittorrent: 1 found, 0 not in client.", landing);
        Assert.DoesNotContain("id=\"check-message\"", await client.GetStringAsync("/torrents"));
    }

    [Fact]
    public async Task Post_CheckClient_QBittorrentUnreachable_ShowsAnErrorAndChangesNothing()
    {
        Func<HttpRequestMessage, HttpResponseMessage> down = _ => throw new HttpRequestException("Connection refused");
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: down);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using var client = fixture.CreateNoRedirectClient();
        var page = await client.GetStringAsync("/torrents");

        var html = await (await PostCheckAsync(client, page, fromHtmx: true)).Content.ReadAsStringAsync();

        Assert.Contains("class=\"notice error\"", html);
        Assert.Contains("http://qbittorrent.test", html);
        Assert.Contains("Connection refused", html);
        Assert.DoesNotContain("not in client", html.Replace("0 not in client", string.Empty)); // never guessed
    }

    [Fact]
    public async Task Post_CheckClient_NoQBittorrentConfigured_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using var client = fixture.CreateNoRedirectClient();
        var page = await client.GetStringAsync("/torrents");

        var html = await (await PostCheckAsync(client, page, fromHtmx: true)).Content.ReadAsStringAsync();

        Assert.Contains("No qBittorrent connection is configured.", html);
    }

    private static string ExtractAntiforgeryToken(string html) =>
        ValueAttributePattern().Match(AntiforgeryInputPattern().Match(html).Value).Groups[1].Value;

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*>")]
    private static partial Regex AntiforgeryInputPattern();

    [GeneratedRegex(@"value=""([^""]*)""")]
    private static partial Regex ValueAttributePattern();
}
