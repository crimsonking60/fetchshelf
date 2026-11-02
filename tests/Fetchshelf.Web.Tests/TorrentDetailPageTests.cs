namespace Fetchshelf.Web.Tests;

public class TorrentDetailPageTests
{
    [Fact]
    public async Task Get_LinkedTorrent_ShowsMetadataLibraryPathAndFiles()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"), libraryPath: "/library/Brandon Sanderson/The Way of Kings");
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/torrents/hash1");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("The Way of Kings", html);
        Assert.Contains("/library/Brandon Sanderson/The Way of Kings", html);
    }

    [Fact]
    public async Task Post_RemoveRecord_DeletesTorrentAndRedirectsToList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();
        var detailHtml = await (await client.GetAsync("/torrents/hash1")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(detailHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var response = await client.PostAsync("/torrents/hash1?handler=RemoveRecord", form);

        response.EnsureSuccessStatusCode();
        var listHtml = await (await client.GetAsync("/torrents")).Content.ReadAsStringAsync();
        Assert.Contains("No torrents found.", listHtml);
    }

    [Fact]
    public async Task Get_TorrentWithMamId_LinksToItOnMam()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents/hash1");

        Assert.Contains("href=\"https://www.myanonamouse.net/t/1\"", html);
    }

    [Fact]
    public async Task Get_TorrentNotInQBittorrent_HasNoCategoryAndTagsForm()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents/hash1");

        Assert.DoesNotContain("category-tags-form", html);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
