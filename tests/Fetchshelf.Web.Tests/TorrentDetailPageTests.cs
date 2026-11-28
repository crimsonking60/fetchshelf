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
    public async Task Get_LinkedFiles_ShowEachSizeReadably_WithTheExactBytesOnHover()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.FetchshelfDbContext>();
            var entity = await dbContext.Torrents.FindAsync("hash1");
            entity!.LinkedFilesJson = System.Text.Json.JsonSerializer.Serialize(
                new List<Fetchshelf.Data.Organizer.LinkedFileRecord> { new("The Way of Kings.m4b", 1_258_291) });
            await dbContext.SaveChangesAsync();
        }

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        Assert.Contains("<span class=\"muted\" title=\"1,258,291 bytes\">(1.2 MiB)</span>", html);
        Assert.DoesNotContain("1258291 bytes", html);
    }

    [Fact]
    public async Task Get_EditedByHand_RefreshAsksFirst_AndTheActionsSayWhatEachRefreshDoes()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings") with { MetadataSource = Fetchshelf.Core.Metadata.MetadataSource.Manual });

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/torrents/hash1"));

        Assert.Contains("data-confirm=\"Replace the details edited by hand with the details from MAM?", html);
        Assert.Contains("Replaces the details with MAM's, including the ones edited by hand", html);
        Assert.Contains("keeping the ones edited by hand", html);
    }

    [Fact]
    public async Task Get_Actions_AreAColumnOfButtons_EachWithWhatItDoesBesideIt()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        var actions = html[html.IndexOf("<div class=\"action-list\">", StringComparison.Ordinal)..html.IndexOf("Edit metadata", StringComparison.Ordinal)];
        var buttons = System.Text.RegularExpressions.Regex.Matches(actions, @"<button[^>]*>([^<]+)</button>\s*<div class=""action-note"">").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["Clean", "Refresh", "Refresh and relink", "Unlink", "Start", "Stop", "Remove from qBittorrent with data", "Remove record"], buttons);
        Assert.DoesNotContain("refresh-explanation", html);
    }

    [Fact]
    public async Task Get_NotEditedByHand_RefreshDoesNotAsk()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        Assert.Contains("Replaces the details with MAM's, without moving any files.", html);
        Assert.DoesNotContain("Replace the details edited by hand", html);
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

    private const string ConfigWithQBittorrent = """
        mam_id = ""

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"
        """;

    [Fact]
    public async Task Get_ShowsMamsFileCountFormatsAndSize_WithoutQBittorrent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var metadata = OrganizerTestSupport.BuildMetadata() with
        {
            MediaType = Fetchshelf.Core.Metadata.MediaType.Ebook,
            FileTypes = ["azw3"],
            FileCount = 1,
            TotalSizeBytes = 2_411_724,
        };
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", metadata);

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        Assert.Contains("<p id=\"files-summary\"><span class=\"label\">Files</span><span class=\"value\">1 file: azw3, 2.3 MiB</span></p>", html);
        Assert.DoesNotContain("client-files", html);
    }

    [Fact]
    public async Task Get_TorrentInQBittorrent_ListsItsFiles_FoldedWhenThereAreMany()
    {
        string[] few = ["Book/Book.epub", "Book/cover.jpg"];
        using (var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer("hash1", fileNames: few)))
        {
            await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

            var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

            Assert.Contains("<details id=\"client-files\" open=\"open\">", html);
            Assert.Contains("<summary>Files in the torrent (2)</summary>", html);
            Assert.Contains("<li><span class=\"mono\">Book/Book.epub</span> <span class=\"muted\" title=\"4 bytes\">(4 B)</span></li>", html);
        }

        var many = Enumerable.Range(1, Fetchshelf.Web.Pages.Torrents.DetailModel.ClientFilesShownOpen + 1).Select(i => $"Issue {i:000}.pdf").ToList();
        using (var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer("hash1", fileNames: many)))
        {
            await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

            var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

            Assert.Contains("<details id=\"client-files\">", html);
            Assert.Contains($"<summary>Files in the torrent ({many.Count})</summary>", html);
        }
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
    public async Task Get_OpenOnMam_IsRightUnderTheTitle()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

        var html = await fixture.CreateClient().GetStringAsync("/torrents/hash1");

        var title = html.IndexOf("<h2>", StringComparison.Ordinal);
        var link = html.IndexOf("<a id=\"mam-link\" href=\"https://www.myanonamouse.net/t/1\"", StringComparison.Ordinal);
        Assert.InRange(link, title, html.IndexOf("class=\"meta-block\"", StringComparison.Ordinal));
        Assert.Contains(">Open on MAM</a>", html);
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
