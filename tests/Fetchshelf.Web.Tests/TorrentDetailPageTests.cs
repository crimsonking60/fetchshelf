using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task Get_NotLinkedBecauseNoLibraryAcceptsIt_SaysWhatToDo()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.FetchshelfDbContext>();
            var torrent = dbContext.Torrents.Single();
            torrent.LibraryMismatchState = "NoLibrary";
            await dbContext.SaveChangesAsync();
        }

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        Assert.Contains("Not linked: no library accepts this torrent's qBittorrent category", html);
    }

    [Fact]
    public async Task Get_TorrentRemovedFromMam_SaysSoAndKeepsTheMamLink()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.FetchshelfDbContext>();
            dbContext.Torrents.Single().RemovedFromMamAt = new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
            await dbContext.SaveChangesAsync();
        }

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/torrents/hash1"));

        Assert.Contains("id=\"removed-from-mam\"", html);
        Assert.Contains("MAM no longer has this torrent (found <time", html);
        Assert.Contains("2026-11-12", html);
        Assert.Contains("id=\"mam-link\"", html);
    }

    [Fact]
    public async Task Get_TorrentStillOnMam_HasNoRemovedNotice()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        Assert.DoesNotContain("id=\"removed-from-mam\"", html);
    }
}
