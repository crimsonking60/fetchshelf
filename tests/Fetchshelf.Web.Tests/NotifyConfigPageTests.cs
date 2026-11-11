using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>The `[[notify]]` blocks on the Config page and in `--check-config`: shown without their secret URLs, with a test button.</summary>
public sealed partial class NotifyConfigPageTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-notify-page-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private const string Config = """
        mam_id = ""
        web_url = "https://fetchshelf.example.net"

        [[notify]]
        name = "Phone"
        url = "https://ntfy.example/secret-topic-x7k2"
        format = "ntfy"
        token = "tk_secret"
        events = ["mam_refused", "job_failing", "grabbed"]

        [[notify]]
        name = "Home Assistant"
        url = "http://homeassistant.example:8123/api/webhook/secret-hook-id"
        events = ["all"]
        """;

    private sealed record Posted(string Url, string? Authorization, string Body);

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Config");
        return AntiforgeryToken().Match(html).Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostTestAsync(HttpClient client, int index)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = await TokenAsync(client) });
        return await client.PostAsync($"/Config?handler=TestNotify&index={index}", form);
    }

    [Fact]
    public async Task ConfigPage_ListsEachBlockWithoutItsUrl()
    {
        using var fixture = new FetchshelfWebAppFixture(Config);

        var html = await fixture.CreateClient().GetStringAsync("/Config");

        var table = html[html.IndexOf("id=\"notify-blocks\"", StringComparison.Ordinal)..];
        Assert.Contains("<td>Phone</td>", table);
        Assert.Contains("<td>ntfy</td>", table);
        Assert.Contains("mam_refused, job_failing, grabbed", table);
        Assert.Contains("<td>Home Assistant</td>", table);
        Assert.Contains("<td>all</td>", table);
        Assert.Contains("(hidden)", table);
        Assert.Equal(2, Regex.Matches(table, ">Send a test message</button>").Count);
        Assert.DoesNotContain("secret-topic", html);
        Assert.DoesNotContain("secret-hook-id", html);
        Assert.DoesNotContain("tk_secret", html);
        Assert.Contains("https://fetchshelf.example.net", html); // web_url is not a secret
    }

    [Fact]
    public async Task ConfigPage_WithoutBlocks_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture();

        Assert.Contains("No [[notify]] entries configured.", await fixture.CreateClient().GetStringAsync("/Config"));
    }

    [Fact]
    public async Task SendATestMessage_PostsToTheBlocksUrlAndShowsTheAnswer()
    {
        var posts = new List<Posted>();
        using var fixture = new FetchshelfWebAppFixture(Config, notifyHandler: request =>
        {
            posts.Add(new Posted(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = fixture.CreateNoRedirectClient();

        var response = await PostTestAsync(client, index: 0);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var post = Assert.Single(posts);
        Assert.Equal(("https://ntfy.example/secret-topic-x7k2", "Bearer tk_secret"), (post.Url, post.Authorization));
        Assert.StartsWith("Fetchshelf test message from ", post.Body, StringComparison.Ordinal);
        var shown = await client.GetStringAsync("/Config"); // the message is shown once, after the redirect
        Assert.Contains("Sent a test message to Phone: HTTP 200.", shown);
        Assert.DoesNotContain("secret-topic", shown);
    }

    [Fact]
    public async Task SendATestMessage_ToABlockThatAnswersWithAnError_ShowsTheStatus()
    {
        using var fixture = new FetchshelfWebAppFixture(Config, notifyHandler: _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = fixture.CreateNoRedirectClient();

        await PostTestAsync(client, index: 1);
        var shown = await client.GetStringAsync("/Config");

        Assert.Contains("Could not send a test message to Home Assistant: HTTP 401.", shown);
        Assert.Contains("class=\"notice error\"", shown);
    }

    [Fact]
    public async Task SendATestMessage_ToABlockThatCannotBeReached_SaysWhyWithoutTheUrl()
    {
        using var fixture = new FetchshelfWebAppFixture(Config, notifyHandler: request =>
            throw new HttpRequestException($"Connection refused ({request.RequestUri})"));
        var client = fixture.CreateNoRedirectClient();

        await PostTestAsync(client, index: 0);
        var shown = await client.GetStringAsync("/Config");

        Assert.Contains("Could not send a test message to Phone: Connection refused", shown);
        Assert.DoesNotContain("secret-topic", shown);
    }

    [Fact]
    public async Task SendATestMessage_ToABlockThatDoesNotExist_IsNotFound()
    {
        using var fixture = new FetchshelfWebAppFixture(Config);
        var client = fixture.CreateNoRedirectClient();

        Assert.Equal(HttpStatusCode.NotFound, (await PostTestAsync(client, index: 5)).StatusCode);
    }

    [Fact]
    public void CheckConfig_ListsTheBlocksAndLinksButNeverTheUrlsOrTokens()
    {
        var path = Path.Combine(_tempDir, "config.toml");
        File.WriteAllText(path, Config);
        using var output = new StringWriter();

        var exitCode = ConfigCheck.Run(path, output);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("Notifications: messages link to https://fetchshelf.example.net", text);
        Assert.Matches(@"Phone\s+ntfy, url set, mam_refused, job_failing, grabbed", text);
        Assert.Matches(@"Home Assistant\s+json, url set, all events", text);
        Assert.DoesNotContain("secret-topic", text);
        Assert.DoesNotContain("secret-hook-id", text);
        Assert.DoesNotContain("tk_secret", text);
    }

    [Fact]
    public void CheckConfig_WithoutAWebUrl_SaysMessagesCarryNoLinks()
    {
        var path = Path.Combine(_tempDir, "config.toml");
        File.WriteAllText(path, "mam_id = \"\"\n[[notify]]\nurl = \"https://x.test/a\"\nevents = [\"all\"]\n");
        using var output = new StringWriter();

        ConfigCheck.Run(path, output);

        Assert.Contains("Notifications: no web_url, so messages carry no links", output.ToString());
        Assert.Matches(@"notify\[0\]\s+json, url set, all events", output.ToString());
    }

    [Fact]
    public void CheckConfig_WithoutBlocks_SaysNothingAboutNotifications()
    {
        var path = Path.Combine(_tempDir, "config.toml");
        File.WriteAllText(path, "mam_id = \"\"\n");
        using var output = new StringWriter();

        ConfigCheck.Run(path, output);

        Assert.DoesNotContain("Notifications", output.ToString());
    }

    [Fact]
    public void CheckConfig_ABadBlock_IsReportedNamingItWithoutItsUrl()
    {
        var path = Path.Combine(_tempDir, "config.toml");
        File.WriteAllText(path, "mam_id = \"\"\n[[notify]]\nname = \"Phone\"\nurl = \"ftp://secret.example/topic\"\nevents = [\"all\"]\n");
        using var output = new StringWriter();

        var exitCode = ConfigCheck.Run(path, output);

        Assert.Equal(1, exitCode);
        Assert.Contains("the [[notify]] 'Phone' has a 'url' that is not an http or https address", output.ToString());
        Assert.DoesNotContain("secret.example", output.ToString());
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""")]
    private static partial Regex AntiforgeryToken();
}
