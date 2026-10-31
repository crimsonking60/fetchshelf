using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 5.4's three named scenarios against a fake qBittorrent server.</summary>
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
    public async Task Post_RefreshAndRelink_MovesFilesToNewFolder()
    {
        var tempDir = Directory.CreateTempSubdirectory("fetchshelf-detail-action-tests-").FullName;
        try
        {
            var libraryDir = Path.Combine(tempDir, "library");
            var oldFolder = Path.Combine(libraryDir, "Brandon Sanderson", "Old Title");
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(Path.Combine(oldFolder, "book.m4b"), "data");

            var originalMetadata = OrganizerTestSupport.BuildMetadata("Old Title");
            var refreshedMetadata = originalMetadata with { Title = "New Title" };
            using var fixture = new FetchshelfWebAppFixture(
                ConfigWithQBittorrent(libraryDir),
                metadataLookup: new FakeMetadataLookup(refreshedMetadata),
                qbittorrentHandler: OrganizerTestSupport.FakeQBittorrentServer(Hash));
            await OrganizerTestSupport.SeedTorrentAsync(fixture, Hash, originalMetadata, libraryPath: oldFolder);
            var client = fixture.CreateClient();

            var detailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
            var token = ExtractAntiforgeryToken(detailHtml);

            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
            var response = await client.PostAsync($"/torrents/{Hash}?handler=RefreshAndRelink", form);
            response.EnsureSuccessStatusCode();

            var newFolder = Path.Combine(libraryDir, "Brandon Sanderson", "New Title");
            Assert.True(File.Exists(Path.Combine(newFolder, "book.m4b")));
            Assert.False(Directory.Exists(oldFolder));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
