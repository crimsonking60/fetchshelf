using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

public partial class JobsPageTests
{
    [Fact]
    public async Task JobsPage_AppStarts_RespondsSuccessfully()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/Jobs");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task JobsPage_NoJobsRegistered_RendersEmptyListNotError()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/Jobs");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("No background jobs are registered yet.", html);
    }

    [Fact]
    public async Task JobsPage_RegisteredJobThatHasRun_ShowsLabelTimeAndResult()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("example-job", _ => Task.CompletedTask);
        await fixture.JobRegistry.TriggerAsync("example-job");
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/Jobs");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("example-job", html);
        Assert.Contains("Success", html);
    }

    [Fact]
    public async Task JobsPage_RegisteredJobWithDescription_ShowsDescriptionByDefault()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("example-job", _ => Task.CompletedTask, "Does the example thing.");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();

        Assert.Contains("Does the example thing.", html);
    }

    [Fact]
    public async Task JobsPage_JobExplanationsDisabled_HidesDescriptionColumn()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\njob_explanations = false\n");
        fixture.JobRegistry.Register("example-job", _ => Task.CompletedTask, "Does the example thing.");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Does the example thing.", html);
    }

    [Fact]
    public async Task RunNow_TriggersJobAndReflectsUpdatedStatus()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("example-job", _ => Task.CompletedTask);
        var client = fixture.CreateClient();

        var jobsHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(jobsHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "example-job",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Jobs?handler=Trigger", form);
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Success", html);
        Assert.NotNull(fixture.JobRegistry.Get("example-job")!.LastRun);
    }

    [Fact]
    public async Task RunNow_LongRunningJob_RespondsImmediatelyShowingRunning()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var jobStarted = new TaskCompletionSource();
        var releaseJob = new TaskCompletionSource();
        fixture.JobRegistry.Register("slow-job", async _ =>
        {
            jobStarted.SetResult();
            await releaseJob.Task;
        });
        var client = fixture.CreateClient();

        var jobsHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(jobsHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "slow-job",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Jobs?handler=Trigger", form);
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Running", html);
        Assert.Null(fixture.JobRegistry.Get("slow-job")!.LastRun);

        await jobStarted.Task;
        releaseJob.SetResult();
    }

    [Fact]
    public async Task JobsPartial_ReturnsCurrentStatusIndependentOfFullPage()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("example-job", _ => Task.CompletedTask);
        await fixture.JobRegistry.TriggerAsync("example-job");
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/Jobs?handler=JobsPartial");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("example-job", html);
        Assert.DoesNotContain("<html", html);
    }

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*>")]
    private static partial Regex AntiforgeryInputPattern();

    [GeneratedRegex(@"value=""([^""]*)""")]
    private static partial Regex ValueAttributePattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var inputMatch = AntiforgeryInputPattern().Match(html);
        if (!inputMatch.Success)
        {
            throw new InvalidOperationException("Antiforgery token input not found in page HTML.");
        }

        var valueMatch = ValueAttributePattern().Match(inputMatch.Value);
        if (!valueMatch.Success)
        {
            throw new InvalidOperationException("Antiforgery token input had no value attribute.");
        }

        return valueMatch.Groups[1].Value;
    }
}
