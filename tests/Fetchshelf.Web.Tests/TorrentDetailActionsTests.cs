using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>The Torrent detail page's actions that reach qBittorrent, against a fake qBittorrent server.</summary>
public partial class TorrentDetailActionsTests
{
    private const string Hash = "hash1";

    private static string ConfigWithQBittorrent(string libraryDir) => $"""
        mam_id = ""

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"

        [[library]]
        library_dir = "{libraryDir.Replace("\\", "\\\\")}"
        category = "audiobooks"
        """;

    [Fact]
    public async Task Post_RefreshAndRelink_DownloadMissing_ShowsWhyAndKeepsTheBook()
    {
        var tempDir = Directory.CreateTempSubdirectory("fetchshelf-detail-action-tests-").FullName;
        try
        {
            var libraryDir = Path.Combine(tempDir, "library");
            var bookFolder = Path.Combine(libraryDir, "Brandon Sanderson", "Old Title");
            Directory.CreateDirectory(bookFolder);
            File.WriteAllText(Path.Combine(bookFolder, "book.m4b"), "data");

            var originalMetadata = OrganizerTestSupport.BuildMetadata("Old Title");
            var missingDownloads = Path.Combine(tempDir, "downloads");
            using var fixture = new FetchshelfWebAppFixture(
                ConfigWithQBittorrent(libraryDir),
                metadataLookup: new FakeMetadataLookup(originalMetadata with { Title = "New Title" }),
                qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer(Hash, missingDownloads, ["book.m4b"]));
            await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, originalMetadata, libraryPath: bookFolder);
            var client = fixture.CreateClient();

            var detailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
            var token = ExtractAntiforgeryToken(detailHtml);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
            var response = await client.PostAsync($"/torrents/{Hash}?handler=RefreshAndRelink", form);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("id=\"action-message\"", html);
            Assert.Contains($"Not relinked: 1 of its files are not in {missingDownloads}; check path_mapping. Nothing was changed.", html);
            Assert.True(File.Exists(Path.Combine(bookFolder, "book.m4b")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Post_Refresh_MamDoesNotFindTheTorrent_SaysNothingWasRefreshed()
    {
        using var fixture = new FetchshelfWebAppFixture(metadataLookup: new FakeMetadataLookup(null));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, OrganizerTestSupport.BuildMetadata());
        var client = fixture.CreateClient();

        var html = await PostAsync(client, "Refresh");

        Assert.Contains("MAM did not find this torrent, so nothing was refreshed.", html);
        Assert.DoesNotContain("Refreshed from MAM", html);
    }

    [Fact]
    public async Task Post_Refresh_MamRateLimits_ShowsAPlainMessageOnTheTorrentPage()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: _ => new HttpResponseMessage((HttpStatusCode)429));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, OrganizerTestSupport.BuildMetadata());
        var client = fixture.CreateClient();

        var html = await PostAsync(client, "Refresh");

        Assert.Contains("Refresh failed: MAM is limiting requests from this account. Wait a few minutes and try again.", html);
    }

    private static async Task<string> PostAsync(HttpClient client, string handler)
    {
        var detailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detailHtml) });
        var response = await client.PostAsync($"/torrents/{Hash}?handler={handler}", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Post_RemoveWithData_CallsQBittorrentDeleteAndRedirectsToList()
    {
        var requestedPaths = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent("/library"),
            qbittorrentHandler: request =>
            {
                requestedPaths.Add(request.RequestUri!.AbsolutePath);
                return OrganizerTestSupport.FakeQBittorrentServer(Hash)(request);
            });
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, libraryPath: "/library/Brandon Sanderson/The Way of Kings");
        var client = fixture.CreateClient();

        var detailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(detailHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var response = await client.PostAsync($"/torrents/{Hash}?handler=RemoveWithData", form);

        response.EnsureSuccessStatusCode();
        Assert.Contains(requestedPaths, p => p.Contains("torrents/delete"));
    }

    [Fact]
    public async Task Get_RemoveWithDataForm_AsksForConfirmationBeforeSubmitting()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent("/library"), qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer(Hash));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync($"/torrents/{Hash}");

        var form = Assert.Single(RemoveWithDataFormPattern().Matches(html)).Value;
        Assert.Contains("onsubmit=\"return confirm(", form);
        Assert.Contains("delete its downloaded files", form);
    }

    [Fact]
    public async Task Post_SetCategoryAndTags_AddsMissingAndRemovesUnticked()
    {
        var addedTags = new List<string>();
        var removedTags = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent("/library"),
            qbittorrentHandler: request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("addTags"))
                {
                    addedTags.Add(request.Content!.ReadAsStringAsync().Result);
                }

                if (request.RequestUri.AbsolutePath.Contains("removeTags"))
                {
                    removedTags.Add(request.Content!.ReadAsStringAsync().Result);
                }

                return OrganizerTestSupport.FakeQBittorrentServer(Hash)(request);
            });
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, libraryPath: "/library/Brandon Sanderson/The Way of Kings");
        var client = fixture.CreateClient();

        var detailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(detailHtml);

        // FakeQBittorrentServer reports the torrent with tags "a,b"; request tags "b,c" => add c, remove a.
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("category", "audiobooks"),
            new KeyValuePair<string, string>("tags", "b"),
            new KeyValuePair<string, string>("tags", "c"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]);
        var response = await client.PostAsync($"/torrents/{Hash}?handler=SetCategoryAndTags", form);

        response.EnsureSuccessStatusCode();
        Assert.Contains(addedTags, t => t.Contains('c'));
        Assert.Contains(removedTags, t => t.Contains('a'));
    }

    [Fact]
    public async Task Get_TorrentInQBittorrent_ShowsCategoryAndTagsFormWithCurrentValues()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent("/library"), qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer(Hash));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync($"/torrents/{Hash}");

        Assert.Contains("id=\"category-tags-form\"", html);
        Assert.Matches("name=\"category\" value=\"audiobooks\"", html);
        Assert.Matches("type=\"checkbox\" name=\"tags\" value=\"a\" checked", html);
        Assert.Matches("type=\"checkbox\" name=\"tags\" value=\"b\" checked", html);
    }

    [Fact]
    public async Task Post_SetCategoryAndTags_AddsCommaSeparatedNewTags()
    {
        var addedTags = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent("/library"),
            qbittorrentHandler: request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("addTags"))
                {
                    addedTags.Add(Uri.UnescapeDataString(request.Content!.ReadAsStringAsync().Result));
                }

                return OrganizerTestSupport.FakeQBittorrentServer(Hash)(request);
            });
        await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash);
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync($"/torrents/{Hash}"));

        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("category", "audiobooks"),
            new KeyValuePair<string, string>("tags", "a"),
            new KeyValuePair<string, string>("tags", "b"),
            new KeyValuePair<string, string>("newTags", "c, d ,"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        ]);
        var response = await client.PostAsync($"/torrents/{Hash}?handler=SetCategoryAndTags", form);

        response.EnsureSuccessStatusCode();
        Assert.Contains(addedTags, t => t.Contains("tags=c,d", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_Unlink_RemovesTheBookFromTheLibraryAndMarksItUnlinked()
    {
        var tempDir = Directory.CreateTempSubdirectory("fetchshelf-detail-unlink-tests-").FullName;
        try
        {
            var bookFolder = Path.Combine(tempDir, "library", "Brandon Sanderson", "The Way of Kings");
            Directory.CreateDirectory(bookFolder);
            File.WriteAllText(Path.Combine(bookFolder, "metadata.json"), "{}");
            using var fixture = new FetchshelfWebAppFixture(
                ConfigWithQBittorrent(Path.Combine(tempDir, "library")), qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer(Hash));
            await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, libraryPath: bookFolder);
            var client = fixture.CreateClient();
            var before = await client.GetStringAsync($"/torrents/{Hash}");
            Assert.Contains("onsubmit=\"return confirm(", System.Text.RegularExpressions.Regex.Match(before, @"<form[^>]*handler=Unlink[^>]*>").Value);

            var html = System.Net.WebUtility.HtmlDecode(await PostAsync(client, "Unlink"));

            Assert.Contains($"Unlinked: removed 0 files from {bookFolder}.", html);
            Assert.Contains("id=\"unlinked-marker\"", html);
            Assert.DoesNotContain("handler=Unlink", html);
            Assert.False(Directory.Exists(bookFolder));
            Assert.Contains("(unlinked)", await client.GetStringAsync("/torrents"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [GeneratedRegex(@"<form[^>]*handler=RemoveWithData[^>]*>")]
    private static partial Regex RemoveWithDataFormPattern();

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenPattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
