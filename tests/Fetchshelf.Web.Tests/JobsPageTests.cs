using System.Net;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task JobsPage_NothingOptionalConfigured_ListsEachKindOnceAlphabeticallyBelowTheJobs()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("Download", _ => Task.CompletedTask);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Jobs");

        var lines = Regex.Matches(html, "<p>([^<]+) is not configured</p>").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["Audiobookshelf", "Autograb", "Hardcover list", "Snatchlist"], lines);
        Assert.True(html.IndexOf("id=\"unconfigured-jobs\"", StringComparison.Ordinal) > html.IndexOf("</table>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JobsPage_ConfiguredKinds_AreNotListedAsUnconfigured()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [audiobookshelf]
            url = "http://abs.test"
            token = "token"

            [[autograb]]
            type = "bookmarks"
            name = "first"

            [[autograb]]
            type = "bookmarks"
            name = "second"
            """);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Jobs");

        Assert.DoesNotContain("Audiobookshelf is not configured", html);
        Assert.DoesNotContain("Autograb is not configured", html);
        Assert.Contains("Snatchlist is not configured", html);
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
    public async Task PostRunNow_RegisteredJob_TriggersItAndShowsUpdatedStatus()
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
    public async Task JobsPage_MultipleJobsRegistered_OrdersDownloadLinkCleanFirstThenAlphabetical()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("Zeta", _ => Task.CompletedTask);
        fixture.JobRegistry.Register("Clean", _ => Task.CompletedTask);
        fixture.JobRegistry.Register("Alpha", _ => Task.CompletedTask);
        fixture.JobRegistry.Register("Link", _ => Task.CompletedTask);
        fixture.JobRegistry.Register("Download", _ => Task.CompletedTask);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();

        var expectedOrder = new[] { "Download", "Link", "Clean", "Alpha", "Zeta" };
        var positions = expectedOrder.Select(name => html.IndexOf(name, StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions, positions.OrderBy(p => p));
    }

    [Fact]
    public async Task GetJobsPartial_JobHasRun_ReturnsCurrentStatusWithoutTheFullPage()
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

    [Fact]
    public async Task JobsPage_JobThatReportedAnOutcome_ShowsTheSummaryItsCountsAndAHistoryLink()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("Link", _ => Task.FromResult<JobOutcome?>(
            new JobOutcome("3 torrents linked", new Dictionary<string, long> { ["linked"] = 3, ["failed"] = 0 })));
        await fixture.JobRegistry.TriggerAsync("Link");
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Jobs");

        Assert.Contains("Success: 3 torrents linked", html);
        Assert.Contains("title=\"linked: 3, failed: 0\"", html);
        Assert.Contains("href=\"/Jobs/History?job=Link\"", html);
        Assert.Contains("(took 0s)", html);
    }

    [Fact]
    public async Task JobsPage_RunningJob_ShowsHowLongItHasRunAndACancelButton()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var started = new TaskCompletionSource();
        fixture.JobRegistry.Register("slow-job", async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var run = fixture.JobRegistry.TriggerAsync("slow-job");
        await started.Task;
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Jobs");

        Assert.Contains("Running for 0s", html);
        Assert.Contains("hx-post=\"?handler=Cancel\"", html);
        Assert.Contains(">Cancel</button>", html);
        Assert.DoesNotContain(">Run now</button>", html);
        fixture.JobRegistry.Cancel("slow-job");
        await run;
    }

    [Fact]
    public async Task JobsPage_JobRunningAgain_ShowsNoResultUntilThisRunEnds()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var runs = 0;
        fixture.JobRegistry.Register("Link", async _ =>
        {
            if (++runs == 1)
            {
                return new JobOutcome("3 torrents linked");
            }

            started.SetResult();
            await release.Task;
            return new JobOutcome("1 torrent linked");
        });
        await fixture.JobRegistry.TriggerAsync("Link");
        var run = fixture.JobRegistry.TriggerAsync("Link");
        await started.Task;
        var client = fixture.CreateClient();

        var running = await client.GetStringAsync("/Jobs?handler=JobsPartial");
        release.SetResult();
        await run;
        var finished = await client.GetStringAsync("/Jobs?handler=JobsPartial");

        Assert.Contains("Running for", running);
        Assert.DoesNotContain("3 torrents linked", running);
        Assert.Matches(@"<td class=""job-result"">\s*</td>", running);
        Assert.Contains("Success: 1 torrent linked", finished);
    }

    [Fact]
    public async Task PostCancel_RunningJob_CancelsIt()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var started = new TaskCompletionSource();
        fixture.JobRegistry.Register("slow-job", async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var run = fixture.JobRegistry.TriggerAsync("slow-job");
        await started.Task;
        var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/Jobs"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "slow-job",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/Jobs?handler=Cancel", form);
        await run;

        response.EnsureSuccessStatusCode();
        Assert.Equal(JobRunResult.Cancelled, fixture.JobRegistry.Get("slow-job")!.LastRun!.Result);
        Assert.Contains("Cancelled", await client.GetStringAsync("/Jobs?handler=JobsPartial"));
    }

    [Fact]
    public async Task PostCancel_ButtonShownForARunThatHasEnded_LeavesTheNewRunGoing()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        fixture.JobRegistry.Register("slow-job", async ct =>
        {
            if (++runs == 2)
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        });
        await fixture.JobRegistry.TriggerAsync("slow-job"); // run 1, which the page's Cancel button was shown for
        var run = fixture.JobRegistry.TriggerAsync("slow-job");
        await started.Task;
        var client = fixture.CreateClient();
        var html = await client.GetStringAsync("/Jobs");
        Assert.Contains("name=\"runNumber\" value=\"2\"", html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "slow-job",
            ["runNumber"] = "1",
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
        });
        var response = await client.PostAsync("/Jobs?handler=Cancel", form);

        response.EnsureSuccessStatusCode();
        Assert.True(fixture.JobRegistry.Get("slow-job")!.IsRunning);
        fixture.JobRegistry.Cancel("slow-job");
        await run;
    }

    [Fact]
    public async Task HistoryPage_RunsOfTheJob_AreListedNewestFirstWithoutOtherJobs()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        AddRuns(
            fixture,
            Run("Link", JobRunResult.Success, "first run", JobTrigger.Startup),
            Run("Clean", JobRunResult.Success, "a Clean run", JobTrigger.Chained),
            Run("Link", JobRunResult.Failure, "second run", JobTrigger.Manual),
            Run("Link", JobRunResult.Success, "third run", JobTrigger.Schedule));

        var response = await client.GetAsync("/Jobs/History?job=Link");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var third = html.IndexOf("third run", StringComparison.Ordinal);
        var second = html.IndexOf("second run", StringComparison.Ordinal);
        var first = html.IndexOf("first run", StringComparison.Ordinal);
        Assert.True(third >= 0 && third < second && second < first);
        Assert.DoesNotContain("a Clean run", html);
        Assert.Contains("Startup", html);
        Assert.Contains("title=\"linked: 2\"", html);
    }

    [Fact]
    public async Task HistoryPage_OnlyUnsuccessful_LeavesOutTheRunsThatSucceeded()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        AddRuns(
            fixture,
            Run("Link", JobRunResult.Success, "went fine", JobTrigger.Schedule),
            Run("Link", JobRunResult.Failure, "qBittorrent unreachable", JobTrigger.Schedule),
            Run("Link", JobRunResult.Cancelled, JobRegistry.CancelledSummary, JobTrigger.Manual),
            Run("Link", JobRunResult.Interrupted, JobRegistry.InterruptedSummary, JobTrigger.Schedule));

        var html = await client.GetStringAsync("/Jobs/History?job=Link&Unsuccessful=true");

        Assert.DoesNotContain("went fine", html);
        Assert.Contains("qBittorrent unreachable", html);
        Assert.Contains(JobRegistry.CancelledSummary, html);
        Assert.Contains(JobRegistry.InterruptedSummary, html);
    }

    [Fact]
    public async Task HistoryPage_MoreRunsThanFitOnAPage_PagesThemNewestFirst()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        AddRuns(fixture, Enumerable.Range(1, 55).Select(i => Run("Link", JobRunResult.Success, $"run number {i}.", JobTrigger.Schedule)).ToArray());

        var first = await client.GetStringAsync("/Jobs/History?job=Link");
        var second = await client.GetStringAsync("/Jobs/History?job=Link&PageNumber=2");

        Assert.Contains("run number 55.", first);
        Assert.Contains("run number 6.", first);
        Assert.DoesNotContain("run number 5.", first);
        Assert.Contains("Page 1 of 2", first);
        Assert.Contains("run number 5.", second);
        Assert.Contains("run number 1.", second);
        Assert.DoesNotContain("run number 6.", second);
    }

    [Fact]
    public async Task HistoryPage_RunWhoseEndIsNotKnown_ShowsNoDuration()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        AddRuns(fixture, Run("Link", JobRunResult.Interrupted, JobRegistry.InterruptedSummary, JobTrigger.Schedule));
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.JobRuns.Single().FinishedAt = null;
            dbContext.SaveChanges();
        }

        var html = await client.GetStringAsync("/Jobs/History?job=Link");

        var row = html[html.IndexOf("<tbody>", StringComparison.Ordinal)..html.IndexOf("</tbody>", StringComparison.Ordinal)];
        Assert.Contains("Interrupted", row);
        Assert.Matches(@"</time></td>\s*<td class=""nowrap""></td>", row);
    }

    private static JobRunEntity Run(string jobName, JobRunResult result, string summary, JobTrigger trigger) => new()
    {
        JobName = jobName,
        StartedAt = new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero),
        FinishedAt = new DateTimeOffset(2026, 11, 11, 8, 1, 5, TimeSpan.Zero),
        Result = result,
        Trigger = trigger,
        Summary = summary,
        CountsJson = "{\"linked\":2}",
    };

    private static void AddRuns(FetchshelfWebAppFixture fixture, params JobRunEntity[] runs)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        foreach (var run in runs)
        {
            // One at a time, so the ids follow the order given.
            dbContext.JobRuns.Add(run);
            dbContext.SaveChanges();
        }
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
