using System.Net;
using System.Net.Http.Json;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 6.1: the AbsMatcher job is registered and scheduled only when `[audiobookshelf]` is configured.</summary>
public class AbsMatcherJobRegistrationTests
{
    private const string ConfigToml = """
        mam_id = "test-mam-id"

        [audiobookshelf]
        url = "http://abs.local"
        token = "abs-token"
        """;

    private static HttpResponseMessage EmptyLibrariesResponse(HttpRequestMessage request) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { libraries = Array.Empty<object>() }) };

    [Fact]
    public async Task JobsPage_WithAudiobookshelfConfigured_ListsAbsMatcherJob()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml, absHandler: EmptyLibrariesResponse);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();

        Assert.Contains("AbsMatcher", html);
    }

    [Fact]
    public async Task RunNow_OnAbsMatcherJob_TriggersSuccessfully()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml, absHandler: EmptyLibrariesResponse);
        var client = fixture.CreateClient();
        var jobsHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(jobsHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "AbsMatcher",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Jobs?handler=Trigger", form);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(fixture.JobRegistry.Get("AbsMatcher")!.LastRun);
    }

    [Fact]
    public async Task JobsPage_WithoutAudiobookshelfConfigured_DoesNotRegisterAbsMatcherJob()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"test-mam-id\"\n");
        var client = fixture.CreateClient();
        await client.GetAsync("/");

        Assert.Null(fixture.JobRegistry.Get("AbsMatcher"));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
