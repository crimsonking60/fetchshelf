namespace Fetchshelf.Web.Tests;

/// <summary>Covers tasks 5.1-5.2: the Torrents list page.</summary>
public class TorrentsIndexPageTests
{
    [Fact]
    public async Task Get_TorrentsListedNewestFirst()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash-old", OrganizerTestSupport.BuildMetadata("Older Book"), createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash-new", OrganizerTestSupport.BuildMetadata("Newer Book"), createdAt: DateTimeOffset.UtcNow);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/torrents")).Content.ReadAsStringAsync();

        Assert.True(html.IndexOf("Newer Book", StringComparison.Ordinal) < html.IndexOf("Older Book", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_FreeTextQuery_NarrowsList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Project Hail Mary"));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/torrents?Q=Hail+Mary")).Content.ReadAsStringAsync();

        Assert.Contains("Project Hail Mary", html);
        Assert.DoesNotContain("The Way of Kings", html);
    }

    [Fact]
    public async Task Get_ColumnValueFilter_NarrowsList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("English Book"));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/torrents?Language=English")).Content.ReadAsStringAsync();

        Assert.Contains("English Book", html);
    }

    [Fact]
    public async Task Post_BulkRemoveRecord_RemovesSelectedTorrents()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();
        var homeHtml = await (await client.GetAsync("/torrents")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(homeHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["selectedIds"] = "hash1",
            ["action"] = "removeRecord",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/torrents?handler=Bulk", form);

        response.EnsureSuccessStatusCode();
        var afterHtml = await (await client.GetAsync("/torrents")).Content.ReadAsStringAsync();
        Assert.Contains("No torrents found.", afterHtml);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
