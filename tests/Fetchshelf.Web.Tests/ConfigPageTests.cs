using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;

namespace Fetchshelf.Web.Tests;

public class ConfigPageTests
{
    private const string ConfigToml = """
        mam_id = "secret-mam-id-123"

        [[qbittorrent]]
        url = "http://localhost:8080"
        username = "admin"
        password = "super-secret-password"

        [[autograb]]
        type = "bookmarks"
        name = "my-autograb"

        [[tag]]
        tags = ["retro-applied"]
        """;

    [Fact]
    public async Task Get_ConfigPage_ShowsEachAutograbsTypeAsTheConfigurationNamesIt()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [[autograb]]
            name = "fl"
            type = "freeleech"

            [[autograb]]
            name = "one uploader"
            type = { uploader = 12345 }
            """);

        var html = await fixture.CreateClient().GetStringAsync("/config");

        // It printed the class name, Fetchshelf.Data.Configuration.AutograbType, for every block.
        Assert.Contains("<td>freeleech</td>", html);
        Assert.Contains("<td>uploader 12345</td>", html);
        Assert.DoesNotContain("AutograbType", html);
    }

    [Fact]
    public async Task Get_ConfigPage_NeverRendersMamIdOrQBittorrentPassword()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/config");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain("secret-mam-id-123", html);
        Assert.DoesNotContain("super-secret-password", html);
        Assert.Contains("my-autograb", html);
    }

    [Fact]
    public async Task Get_ConfigPage_ShowsEachLibrarysFolderNamesAndTemplate()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [[library]]
            library_dir = "/library/audiobooks"
            category = "audiobooks"

            [[library]]
            library_dir = "/library/comics"
            category = "comics"
            folder_names = "portable"
            folder_template = "{series}/{title}"

            [[library]]
            library_dir = "/library/manga"
            category = "manga"
            layout = "series"
            oneshots_dir = "One-Shots"
            """);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/config");

        Assert.Contains("<td>posix</td>", html);
        Assert.Contains("<td>book, default template</td>", html);
        Assert.Contains("<td>portable</td>", html);
        Assert.Contains("<td>{series}/{title}</td>", html);
        Assert.Contains("<td>series; books without a series in One-Shots</td>", html);
        Assert.Contains("<td>series.json</td>", html);
    }

    [Fact]
    public async Task Get_ConfigPage_ShowsEachBlocksBuffersAndCap_OrWhatAppliesWithoutThem()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""
            unsat_buffer = 12
            wedge_buffer = 4

            [[autograb]]
            name = "Careful"
            type = "bookmarks"
            unsat_buffer = 30
            max_active_downloads = 5

            [hardcover]
            token = "t"

            [[hardcover_list]]
            status = "want_to_read"
            wedge_buffer = 9

            [[hardcover_list.grab]]
            cost = "free"

            [follow]
            max_active_downloads = 2

            [[follow.grab]]
            cost = "free"

            [upgrade]
            """);

        var html = await fixture.CreateClient().GetStringAsync("/config");

        Assert.Contains("<th>Unsat buffer</th><th>Wedge buffer</th><th>Max waiting</th>", html);
        // The autograb, the Hardcover list, [follow] and [upgrade], in that order.
        Assert.Matches(@"<td>30</td>\s*<td>global \(4\)</td>\s*<td>5</td>[\s\S]*"
            + @"<td>global \(12\)</td>\s*<td>9</td>\s*<td>no cap</td>[\s\S]*"
            + @"<td>global \(12\)</td>\s*<td>global \(4\)</td>\s*<td>2</td>[\s\S]*"
            + @"<td>global \(12\)</td>\s*<td>global \(4\)</td>\s*<td>no cap</td>", html);
    }

    [Fact]
    public async Task Get_ConfigPage_ListsHardcoverLists_ButNeverTheToken()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [hardcover]
            token = "secret-hardcover-token"

            [[hardcover_list]]
            name = "Wishlist"
            list = "books-to-fetch"
            prefer_format = "ebook"

            [[hardcover_list.grab]]
            cost = "wedge"
            """);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/config");

        Assert.DoesNotContain("secret-hardcover-token", html);
        Assert.Contains("Wishlist", html);
        Assert.Contains("list books-to-fetch", html);
        Assert.Contains("Ebook", html);
        Assert.Contains("Wedge", html);
    }

    [Fact]
    public async Task Get_ConfigPage_ShowsAudiobookshelfHardcoverAndDownloadAfterSelection_WithoutTheTokens()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""
            download_after_selection = false

            [audiobookshelf]
            url = "http://abs.test"
            token = "secret-abs-token"
            interval = 30

            [hardcover]
            token = "secret-hardcover-token"
            """);

        var html = await fixture.CreateClient().GetStringAsync("/config");

        Assert.Contains("<tr><th>download_after_selection</th><td>False</td></tr>", html);
        Assert.Matches("<table id=\"audiobookshelf\">.*http://abs.test.*<th>interval</th><td>30</td>", html.ReplaceLineEndings(" "));
        Assert.Contains("<table id=\"hardcover\">", html);
        Assert.DoesNotContain("secret-abs-token", html);
        Assert.DoesNotContain("secret-hardcover-token", html);
    }

    [Fact]
    public async Task Post_TriggerAutograbFromConfigPage_RunsThatAutograbbersJob()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();
        await client.GetAsync("/"); // forces the host to start, running startup job registration

        var configHtml = await (await client.GetAsync("/config")).Content.ReadAsStringAsync();
        Assert.Contains("action=\"/Jobs?handler=Trigger\"", configHtml);
        var token = ExtractAntiforgeryToken(configHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "my-autograb",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Jobs?handler=Trigger", form);

        response.EnsureSuccessStatusCode();
        await WaitForJobToFinishAsync(fixture, "my-autograb");
        Assert.NotNull(fixture.JobRegistry.Get("my-autograb")!.LastRun);
    }

    /// <summary>
    /// "Run now" starts the job without waiting for it to finish (see Jobs/Index.cshtml.cs), so tests that
    /// care about the outcome must poll instead of asserting immediately after the POST.
    /// </summary>
    private static async Task WaitForJobToFinishAsync(FetchshelfWebAppFixture fixture, string jobName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (fixture.JobRegistry.Get(jobName)!.LastRun is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Post_ApplyTagRuleFromConfigPage_UpdatesMatchingStoredTorrentsCategoryAndTags()
    {
        var categoryCalls = new List<(string Hash, string Category)>();
        var tagCalls = new List<(string Hash, string Tags)>();
        using var fixture = new FetchshelfWebAppFixture(
            ConfigToml, qbittorrentHandler: RecordingQBittorrentServer("hash1", categoryCalls, tagCalls));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata());
        var client = fixture.CreateClient();

        var configHtml = await (await client.GetAsync("/config")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(configHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ruleIndex"] = "0",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/config?handler=ApplyTagRule&ruleIndex=0", form);

        response.EnsureSuccessStatusCode();
        var tagCall = Assert.Single(tagCalls);
        Assert.Equal("hash1", tagCall.Hash);
        Assert.Equal("retro-applied", tagCall.Tags);
        Assert.Empty(categoryCalls); // the configured rule has no category, only tags
        Assert.Contains("Applied the tag rule to 1 torrent.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_PreviewTagRule_ShowsWhatWouldChange_WithoutChangingIt_ThenApplyChangesIt()
    {
        var categoryCalls = new List<(string Hash, string Category)>();
        var tagCalls = new List<(string Hash, string Tags)>();
        using var fixture = new FetchshelfWebAppFixture(
            ConfigToml, qbittorrentHandler: RecordingQBittorrentServer("hash1", categoryCalls, tagCalls));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));
        var client = fixture.CreateClient();
        var configHtml = await client.GetStringAsync("/config");
        Assert.Contains("Preview applying to existing torrents", configHtml);
        var token = ExtractAntiforgeryToken(configHtml);

        using var previewForm = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var preview = await (await client.PostAsync("/config?handler=PreviewTagRule&ruleIndex=0", previewForm)).Content.ReadAsStringAsync();

        Assert.Contains("id=\"tag-rule-preview\"", preview);
        Assert.Contains("1 torrent would change.", preview);
        Assert.Contains("The Way of Kings", preview);
        Assert.Contains("retro-applied", preview);
        Assert.Contains("Apply to this torrent", preview);
        Assert.Empty(tagCalls);

        using var applyForm = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractAntiforgeryToken(preview) });
        var applied = await client.PostAsync("/config?handler=ApplyTagRule&ruleIndex=0", applyForm);

        Assert.Contains("Applied the tag rule to 1 torrent.", await applied.Content.ReadAsStringAsync());
        Assert.Equal(("hash1", "retro-applied"), Assert.Single(tagCalls));
    }

    [Fact]
    public async Task Get_TagRule_ShowsItsWholeFilter_NotOnlyItsCategories()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [[tag]]
            name = "Comics"
            media_type = ["ComicBook", "Manga"]
            languages = ["English"]
            category = "comics"
            """);

        var html = await fixture.CreateClient().GetStringAsync("/config");

        Assert.Contains("<th class=\"phone-title\">#</th><th class=\"phone-title\">Name</th><th>Matches</th>", html);
        Assert.Matches(@"<td>1</td>\s*<td>Comics</td>", html);
        Assert.Contains("Media types: ComicBook, Manga<br />", html);
        Assert.Contains("Languages: English<br />", html);
        Assert.Contains("Categories: audio (all), ebook (all)<br />", html);
    }

    [Fact]
    public async Task Post_PreviewTagRule_FromHtmx_AnswersWithJustThePreview_ForTheDialog()
    {
        var tagCalls = new List<(string Hash, string Tags)>();
        using var fixture = new FetchshelfWebAppFixture(ConfigToml, qbittorrentHandler: RecordingQBittorrentServer("hash1", [], tagCalls));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings"));
        var client = fixture.CreateClient();
        var configHtml = await client.GetStringAsync("/config");
        Assert.Contains("<dialog id=\"tag-rule-dialog\"", configHtml);
        Assert.Contains("hx-target=\"#tag-rule-dialog-body\"", configHtml);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/config?handler=PreviewTagRule&ruleIndex=0")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractAntiforgeryToken(configHtml) }),
        };
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.StartsWith("<div id=\"tag-rule-preview\">", body.TrimStart());
        Assert.DoesNotContain("<html", body);
        // The rule it is for comes first, since the dialog covers the table: unnamed, it is called by its number.
        Assert.Contains("<h4>Applying tag rule 1 to existing torrents</h4>", body);
        var rule = body[body.IndexOf("id=\"tag-rule-preview-rule\"", StringComparison.Ordinal)..];
        Assert.Contains("Categories: audio (all), ebook (all)<br />", rule);
        Assert.Contains("<td>retro-applied</td>", rule);
        Assert.Contains("1 torrent would change.", body);
        Assert.Contains("Apply to this torrent", body);
        var action = System.Text.RegularExpressions.Regex.Match(body, @"<form[^>]*action=""([^""]*ApplyTagRule[^""]*)""").Groups[1].Value;
        Assert.Contains("ruleIndex=0", action);
        Assert.Empty(tagCalls);
    }

    [Fact]
    public async Task Post_PreviewTagRule_FromHtmx_QBittorrentFails_SaysSoInTheDialog()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml, qbittorrentHandler: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/config"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/config?handler=PreviewTagRule&ruleIndex=0")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
        };
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // htmx only swaps successful answers
        Assert.Contains("Previewing the tag rule failed: MAM or qBittorrent could not be reached", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_PreviewTagRule_NamedRule_IsCalledByItsNumberAndName()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [[tag]]
            tags = ["first"]

            [[tag]]
            name = "Comics"
            media_type = ["ComicBook"]
            category = "comics"
            """);
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/config"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/config?handler=PreviewTagRule&ruleIndex=1")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
        };
        request.Headers.Add("HX-Request", "true");
        var body = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("<h4>Applying tag rule 2 (Comics) to existing torrents</h4>", body);
        Assert.Contains("Media types: ComicBook<br />", body);
        Assert.Contains("<td>comics</td>", body);
    }

    [Fact]
    public async Task Post_PreviewTagRule_UnknownRule_IsNotFound()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/config"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var response = await client.PostAsync("/config?handler=PreviewTagRule&ruleIndex=5", form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ApplyTagRule_QBittorrentFails_ShowsAPlainMessageOnTheConfigPage()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigToml, qbittorrentHandler: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata());
        var client = fixture.CreateClient();

        var token = ExtractAntiforgeryToken(await (await client.GetAsync("/config")).Content.ReadAsStringAsync());
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        var response = await client.PostAsync("/config?handler=ApplyTagRule&ruleIndex=0", form);

        response.EnsureSuccessStatusCode();
        Assert.Contains(
            "Applying the tag rule stopped part-way: MAM or qBittorrent could not be reached or answered with an error.",
            await response.Content.ReadAsStringAsync());
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> RecordingQBittorrentServer(
        string knownHash, List<(string Hash, string Category)> categoryCalls, List<(string Hash, string Tags)> tagCalls) => request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("auth/login"))
        {
            var loginResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            loginResponse.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
            return loginResponse;
        }

        if (path.Contains("torrents/info"))
        {
            var torrents = new List<QBittorrentTorrent>
            {
                new() { Hash = knownHash, Name = "Book", SavePath = "/downloads", State = "uploading" },
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
        }

        if (path.Contains("setCategory"))
        {
            var form = ParseForm(request);
            categoryCalls.Add((form["hashes"], form["category"]));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
        }

        if (path.Contains("addTags"))
        {
            var form = ParseForm(request);
            tagCalls.Add((form["hashes"], form["tags"]));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
    };

    private static Dictionary<string, string> ParseForm(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        var result = new Dictionary<string, string>();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
        }

        return result;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    [Fact]
    public async Task Get_ConfigPage_ShowsTheBonusSettings()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [bonus]
            keep_points = 55000
            upload_amounts = [100, 50]
            wedge_every = 24
            """);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/config");

        Assert.Contains("<td>55000</td>", html);
        Assert.Contains("<td>100 GB, 50 GB</td>", html);
        Assert.Contains("<td>24 hours</td>", html);
    }
}
