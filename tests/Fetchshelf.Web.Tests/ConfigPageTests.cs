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
}
