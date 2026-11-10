using System.Text.RegularExpressions;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public class TorrentsIndexPageTests
{
    [Fact]
    public async Task Get_SearchInLowerCase_FindsTitlesAndAuthorsInAnyCase()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Beautiful American", ["Jeanne Mackin"]));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Unrelated Book", ["Someone Else"]));
        using var client = fixture.CreateClient();

        var byTitle = await client.GetStringAsync("/torrents?Q=beautiful+american");
        var byAuthor = await client.GetStringAsync("/torrents?Q=mackin");

        Assert.Contains("The Beautiful American", byTitle);
        Assert.DoesNotContain("Unrelated Book", byTitle);
        Assert.Contains("The Beautiful American", byAuthor);
    }

    [Fact]
    public async Task Get_SeveralTorrents_ListsNewestFirst()
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

    [Fact]
    public async Task Get_MediaTypeValue_IsAFilterLink_ThatNarrowsList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("Audio Book"));
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash2", OrganizerTestSupport.BuildMetadata("Paper Book") with { MediaType = Fetchshelf.Core.Metadata.MediaType.Ebook });
        var client = fixture.CreateClient();

        var listHtml = await client.GetStringAsync("/torrents");
        var filteredHtml = await client.GetStringAsync("/torrents?MediaType=Ebook");

        Assert.Contains("MediaType=Ebook", listHtml);
        Assert.Contains("Paper Book", filteredHtml);
        Assert.DoesNotContain("Audio Book", filteredHtml);
    }

    [Fact]
    public async Task Get_SortAndFilterLinks_KeepTheSearchAndOtherFilters()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));
        var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/torrents?Q=Kings&Language=English"));

        var titleSortLink = System.Text.RegularExpressions.Regex.Match(html, @"href=""([^""]*Sort=Title[^""]*)""").Groups[1].Value;
        Assert.Contains("Q=Kings", titleSortLink);
        Assert.Contains("Language=English", titleSortLink);
        Assert.Contains("Dir=asc", titleSortLink);
        var mediaTypeLink = System.Text.RegularExpressions.Regex.Match(html, @"href=""([^""]*MediaType=Audiobook[^""]*)""").Groups[1].Value;
        Assert.Contains("Q=Kings", mediaTypeLink);
        Assert.Contains("Language=English", mediaTypeLink);
    }

    [Fact]
    public async Task Get_Filtered_DropdownShowsTheFilter_AndClearFiltersKeepsTheSearch()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/torrents?Q=Kings&Language=English"));

        Assert.Contains("<option value=\"English\" selected=\"selected\">English</option>", html);
        var clearLink = System.Text.RegularExpressions.Regex.Match(html, @"<a id=""clear-filters"" href=""([^""]*)""").Groups[1].Value;
        Assert.Contains("Q=Kings", clearLink);
        Assert.DoesNotContain("Language=", clearLink);
    }

    [Fact]
    public async Task Get_Unfiltered_HasNoClearFiltersLink_AndOffersTheValuesInUse()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash1", OrganizerTestSupport.BuildMetadata("Paper Book") with { MediaType = Fetchshelf.Core.Metadata.MediaType.Ebook });
        await SeedAsync(fixture, "hash2", "Audio Book", t => { t.LibraryPath = "/library/a"; t.LinkerName = "Audiobooks"; });
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?Q=Kings");

        Assert.DoesNotContain("id=\"clear-filters\"", html);
        Assert.Contains("<option value=\"Ebook\">Ebook</option>", html);
        Assert.Contains("<option value=\"Audiobook\">Audiobook</option>", html);
        Assert.Contains("<option value=\"Audiobooks\">Audiobooks</option>", html);
    }

    [Theory]
    [InlineData("linked", "Linked Book")]
    [InlineData("waiting", "Waiting Book")]
    [InlineData("replaced", "Replaced Book")]
    [InlineData("unlinked", "Unlinked Book")]
    [InlineData("metadata-only", "Metadata Book")]
    public async Task Get_StatusFilter_ShowsOnlyTorrentsInThatState(string status, string expected)
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAllStatusesAsync(fixture);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync($"/torrents?Status={status}");

        foreach (var title in AllStatusTitles)
        {
            if (title == expected)
            {
                Assert.Contains(title, html);
            }
            else
            {
                Assert.DoesNotContain(title, html);
            }
        }
    }

    [Fact]
    public async Task Get_NotLinkedFilter_ShowsEverythingButLinkedTorrents()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAllStatusesAsync(fixture);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?Status=not-linked");

        Assert.DoesNotContain("Linked Book", html.Replace("Unlinked Book", string.Empty, StringComparison.Ordinal));
        foreach (var title in AllStatusTitles.Where(t => t != "Linked Book"))
        {
            Assert.Contains(title, html);
        }
    }

    [Fact]
    public async Task Get_QbCategoryFilter_ShowsOnlyThatCategory_AndOffersTheCategoriesInUse()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, "hash1", "Ebook One", t => t.QBittorrentCategory = "fetchshelf-ebooks");
        await SeedAsync(fixture, "hash2", "Audiobook One", t => t.QBittorrentCategory = "fetchshelf-audiobooks");
        await SeedAsync(fixture, "hash3", "No Category", _ => { });
        var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/torrents?QbCategory=fetchshelf-ebooks"));

        Assert.Contains("Ebook One", html);
        Assert.DoesNotContain("Audiobook One", html);
        Assert.DoesNotContain("No Category", html);
        Assert.Contains("<option value=\"fetchshelf-ebooks\" selected=\"selected\">fetchshelf-ebooks</option>", html);
        Assert.Contains("<option value=\"fetchshelf-audiobooks\">fetchshelf-audiobooks</option>", html);
        var clearLink = Regex.Match(html, @"<a id=""clear-filters"" href=""([^""]*)""").Groups[1].Value;
        Assert.DoesNotContain("QbCategory", clearLink);
    }

    [Fact]
    public async Task Get_LibraryAndProblemFilters_Combine()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, "hash1", "Moved Audiobook", t => { t.LibraryPath = "/a"; t.LinkerName = "Audiobooks"; t.LibraryMismatchState = "NewPath"; });
        await SeedAsync(fixture, "hash2", "Fine Audiobook", t => { t.LibraryPath = "/b"; t.LinkerName = "Audiobooks"; });
        await SeedAsync(fixture, "hash3", "Moved Ebook", t => { t.LibraryPath = "/c"; t.LinkerName = "Ebooks"; t.LibraryMismatchState = "NewPath"; });
        await SeedAsync(fixture, "hash4", "Missing Ebook", t => { t.LibraryPath = "/d"; t.LinkerName = "Ebooks"; t.ClientStatus = "not in client"; });
        var client = fixture.CreateClient();

        var audiobookProblems = await client.GetStringAsync("/torrents?Library=Audiobooks&Problem=any");
        var missing = await client.GetStringAsync("/torrents?Problem=not-in-client");

        Assert.Contains("Moved Audiobook", audiobookProblems);
        Assert.DoesNotContain("Fine Audiobook", audiobookProblems);
        Assert.DoesNotContain("Moved Ebook", audiobookProblems);
        Assert.Contains("Missing Ebook", missing);
        Assert.DoesNotContain("Moved Ebook", missing);
    }

    [Fact]
    public async Task Get_RemovedFromMamFilter_ShowsOnlyThoseAndLabelsThem()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, "hash1", "Gone From MAM", t => { t.ClientStatus = "uploading"; t.RemovedFromMamAt = DateTimeOffset.UtcNow; });
        await SeedAsync(fixture, "hash2", "Still On MAM", t => t.ClientStatus = "uploading");
        var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/torrents?Problem=removed-from-mam"));

        Assert.Contains("Gone From MAM", html);
        Assert.DoesNotContain("Still On MAM", html);
        Assert.Contains("(removed from MAM)", html);
        Assert.Contains("<option value=\"removed-from-mam\" selected=\"selected\">Removed from MAM</option>", html);
    }

    [Fact]
    public async Task Post_Bulk_ComesBackToTheSameFilters()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateNoRedirectClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/torrents"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["selectedIds"] = "hash1",
            ["action"] = "removeRecord",
            ["Status"] = "waiting",
            ["Library"] = "Audiobooks",
            ["Problem"] = "mismatch",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/torrents?handler=Bulk", form);

        var location = response.Headers.Location!.ToString();
        Assert.Contains("Status=waiting", location);
        Assert.Contains("Library=Audiobooks", location);
        Assert.Contains("Problem=mismatch", location);
    }

    private static readonly string[] AllStatusTitles = ["Linked Book", "Waiting Book", "Replaced Book", "Unlinked Book", "Metadata Book"];

    private static async Task SeedAllStatusesAsync(FetchshelfWebAppFixture fixture)
    {
        await SeedAsync(fixture, "hash1", "Linked Book", t => t.LibraryPath = "/library/Linked");
        await SeedAsync(fixture, "hash2", "Waiting Book", _ => { });
        await SeedAsync(fixture, "hash3", "Replaced Book", t => { t.ReplacedWithTorrentId = "hash1"; t.ReplacedAt = DateTimeOffset.UtcNow; });
        await SeedAsync(fixture, "hash4", "Unlinked Book", t => t.UnlinkedAt = DateTimeOffset.UtcNow);
        await SeedAsync(fixture, "mam:5", "Metadata Book", _ => { });
    }

    private static async Task SeedAsync(FetchshelfWebAppFixture fixture, string id, string title, Action<TorrentEntity> configure)
    {
        await OrganizerTestSupport.SeedTorrentAsync(fixture, id, OrganizerTestSupport.BuildMetadata(title));
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var entity = await dbContext.Torrents.SingleAsync(t => t.Id == id);
        configure(entity);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Post_BulkRelink_RelinksByStoredDetailsAndSaysWhyTheRestWasNot()
    {
        var tempDir = Directory.CreateTempSubdirectory("fetchshelf-bulk-relink-tests-").FullName;
        try
        {
            var downloads = Path.Combine(tempDir, "downloads");
            var libraryDir = Path.Combine(tempDir, "library");
            Directory.CreateDirectory(downloads);
            File.WriteAllText(Path.Combine(downloads, "book.m4b"), "data");
            var metadata = OrganizerTestSupport.BuildMetadata("The Way of Kings");
            using var fixture = new FetchshelfWebAppFixture(
                $$"""
                mam_id = ""

                [[qbittorrent]]
                url = "http://qbittorrent.test"
                username = "admin"
                password = "adminadmin"

                [[library]]
                library_dir = "{{libraryDir.Replace("\\", "\\\\")}}"
                category = "audiobooks"
                """,
                metadataLookup: new FakeMetadataLookup(null), // MAM finds nothing: the stored details must be enough
                qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer("hash1", downloads, ["book.m4b"]));
            await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", metadata);
            await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Gone From Client"));
            var client = fixture.CreateClient();
            var token = ExtractAntiforgeryToken(await client.GetStringAsync("/torrents"));

            using var form = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("selectedIds", "hash1"),
                new KeyValuePair<string, string>("selectedIds", "hash2"),
                new KeyValuePair<string, string>("action", "relink"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]);
            var response = await client.PostAsync("/torrents?handler=Bulk", form);

            response.EnsureSuccessStatusCode();
            var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains(
                "Relinked 1 of 2 torrents. Gone From Client: Not relinked: It is not in qBittorrent, so there is nothing to link from. Nothing was changed.",
                html);
            Assert.True(File.Exists(Path.Combine(libraryDir, "Brandon Sanderson", "The Way of Kings", "book.m4b")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Post_BulkUnlink_UnlinksEachAndSaysHowMany()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Second Book"));
        var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/torrents");
        Assert.Matches(@"<button[^>]*value=""unlink""[^>]*onclick=""return confirm\(", page);

        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("selectedIds", "hash1"),
            new KeyValuePair<string, string>("selectedIds", "hash2"),
            new KeyValuePair<string, string>("action", "unlink"),
            new KeyValuePair<string, string>("__RequestVerificationToken", ExtractAntiforgeryToken(page)),
        ]);
        var response = await client.PostAsync("/torrents?handler=Bulk", form);

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unlinked 2 torrents.", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, @"\(unlinked\)").Count);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private const string ConfigWithLibraries = """
        mam_id = ""

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"

        [[library]]
        name = "Ebooks"
        library_dir = "/library/ebooks"
        category = "fetchshelf-ebooks"

        [[library]]
        name = "Audiobooks"
        library_dir = "/library/audiobooks"
        category = "fetchshelf-audiobooks"

        [[library]]
        library_dir = "/library/other"
        download_dir = "/downloads/other"
        """;

    [Fact]
    public async Task Get_OffersTheCategoriesTheLibrariesUse()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithLibraries);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

        var html = await fixture.CreateClient().GetStringAsync("/torrents");

        var start = html.IndexOf("id=\"bulk-category\"", StringComparison.Ordinal);
        var select = html[start..html.IndexOf("</select>", start, StringComparison.Ordinal)];
        var options = Regex.Matches(select, @"<option value=""([^""]*)""[^>]*>([^<]*)</option>").Select(m => m.Groups[2].Value);
        Assert.Equal(["Choose a category", "fetchshelf-ebooks (Ebooks)", "fetchshelf-audiobooks (Audiobooks)"], options);
    }

    [Fact]
    public async Task Get_NoLibraryUsesACategory_HasNoCategoryDropdown()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

        var html = await fixture.CreateClient().GetStringAsync("/torrents");

        Assert.DoesNotContain("id=\"bulk-category\"", html);
    }

    [Fact]
    public async Task Post_BulkSetCategory_SetsItInQBittorrentAndShowsIt()
    {
        var categoriesSet = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(ConfigWithLibraries, qbittorrentHandler: request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("setCategory"))
            {
                categoriesSet.Add(Uri.UnescapeDataString(request.Content!.ReadAsStringAsync().Result));
            }

            return OrganizerTestSupport.FakeQBittorrentServer("hash1")(request);
        });
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/torrents"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["selectedIds"] = "hash1",
            ["action"] = "setCategory",
            ["category"] = "fetchshelf-ebooks",
            ["__RequestVerificationToken"] = token,
        });
        var html = System.Net.WebUtility.HtmlDecode(await (await client.PostAsync("/torrents?handler=Bulk", form)).Content.ReadAsStringAsync());

        Assert.Contains(categoriesSet, c => c.Contains("hashes=hash1", StringComparison.Ordinal) && c.Contains("category=fetchshelf-ebooks", StringComparison.Ordinal));
        Assert.Contains("Set the category to fetchshelf-ebooks for 1 torrent.", html);
        using var scope = fixture.CreateScope();
        Assert.Equal("fetchshelf-ebooks", scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Torrents.Single().QBittorrentCategory);
    }

    [Fact]
    public async Task Post_BulkSetCategory_WithoutALibraryCategory_ChangesNothing()
    {
        var categoriesSet = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(ConfigWithLibraries, qbittorrentHandler: request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("setCategory"))
            {
                categoriesSet.Add(request.RequestUri.AbsolutePath);
            }

            return OrganizerTestSupport.FakeQBittorrentServer("hash1")(request);
        });
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/torrents"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["selectedIds"] = "hash1",
            ["action"] = "setCategory",
            ["category"] = "",
            ["__RequestVerificationToken"] = token,
        });
        var html = await (await client.PostAsync("/torrents?handler=Bulk", form)).Content.ReadAsStringAsync();

        Assert.Empty(categoriesSet);
        Assert.Contains("Choose one of your libraries", html);
    }
}
