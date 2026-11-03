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
    public async Task Get_Filtered_SearchFormKeepsFilter_AndShowAllClearsIt()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/torrents?Q=Kings&Language=English"));

        Assert.Contains("<input type=\"hidden\" name=\"Language\" value=\"English\" />", html);
        var activeFilters = html[html.IndexOf("id=\"active-filters\"", StringComparison.Ordinal)..];
        var showAllLink = System.Text.RegularExpressions.Regex.Match(activeFilters, @"href=""([^""]*)""").Groups[1].Value;
        Assert.Contains("Q=Kings", showAllLink);
        Assert.DoesNotContain("Language=", showAllLink);
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
}
