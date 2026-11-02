using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>The real "Link"/"Clean" jobs are visible and triggerable.</summary>
public partial class OrganizerJobRegistrationTests
{
    [Fact]
    public async Task JobsPage_WithBackgroundJobsEnabled_ListsLinkAndCleanJobs()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"test-mam-id\"\n");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();

        Assert.Contains("Link", html);
        Assert.Contains("Clean", html);
    }

    [Fact]
    public async Task RunNow_OnLinkJob_TriggersSuccessfully()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"test-mam-id\"\n");
        var client = fixture.CreateClient();

        var jobsHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(jobsHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "Link",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Jobs?handler=Trigger", form);
        response.EnsureSuccessStatusCode();

        Assert.NotNull(fixture.JobRegistry.Get("Link")!.LastRun);
        Assert.NotNull(fixture.JobRegistry.Get("Clean")!.LastRun);
    }

    [Fact]
    public async Task JobsPage_WithEmptyMamId_DoesNotRegisterOrganizerJobs()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();

        Assert.Contains("No background jobs are registered yet.", html);
    }

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*>")]
    private static partial Regex AntiforgeryInputPattern();

    [GeneratedRegex(@"value=""([^""]*)""")]
    private static partial Regex ValueAttributePattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var inputMatch = AntiforgeryInputPattern().Match(html);
        var valueMatch = ValueAttributePattern().Match(inputMatch.Value);
        return valueMatch.Groups[1].Value;
    }
}
