using System.Text.RegularExpressions;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary><b>Dry run</b> on the Jobs page, and a run's details in its history.</summary>
public sealed partial class JobsDryRunTests
{
    private static string Token(string html) => AntiforgeryToken().Match(html).Groups[1].Value;

    [Fact]
    public async Task JobsPage_OffersDryRun_OnlyForJobsThatHaveOne()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register("with-dry-run", _ => Task.FromResult<JobOutcome?>(null), dryRunAsync: _ => Task.FromResult<JobOutcome?>(null));
        fixture.JobRegistry.Register("without", _ => Task.FromResult<JobOutcome?>(null));

        var html = await fixture.CreateClient().GetStringAsync("/Jobs");

        var rows = RowPattern().Matches(html).Select(m => m.Value).ToList();
        Assert.Contains(rows, row => row.Contains("with-dry-run", StringComparison.Ordinal) && row.Contains(">Dry run</button>", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains(">without<", StringComparison.Ordinal) && !row.Contains(">Dry run</button>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostDryRun_RunsTheDryRun_NotTheRealOne()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var (real, dry) = (0, 0);
        fixture.JobRegistry.Register(
            "example-job",
            _ => { real++; return Task.FromResult<JobOutcome?>(null); },
            dryRunAsync: _ => { dry++; return Task.FromResult<JobOutcome?>(new JobOutcome("Dry run: 1 would have been selected")); });
        var client = fixture.CreateClient();
        var token = Token(await client.GetStringAsync("/Jobs"));

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "example-job",
            ["dryRun"] = "true",
            ["__RequestVerificationToken"] = token,
        });
        (await client.PostAsync("/Jobs?handler=Trigger", form)).EnsureSuccessStatusCode();
        await fixture.JobRegistry.RunsEndedAsync();

        Assert.Equal((0, 1), (real, dry));
        Assert.Equal(JobTrigger.DryRun, fixture.JobRegistry.Get("example-job")!.LastRun!.Trigger);
    }

    [Fact]
    public async Task PostDryRun_ForAJobWithoutOne_RunsNothing()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var real = 0;
        fixture.JobRegistry.Register("Link", _ => { real++; return Task.FromResult<JobOutcome?>(null); });
        var client = fixture.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["jobName"] = "Link",
            ["dryRun"] = "true",
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync("/Jobs")),
        });

        (await client.PostAsync("/Jobs?handler=Trigger", form)).EnsureSuccessStatusCode();
        await fixture.JobRegistry.RunsEndedAsync();

        Assert.Equal(0, real);
    }

    [Fact]
    public async Task ConfiguredDryRunBlock_GetsNoSeparateDryRun_AndItsDescriptionSaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = "test-mam-id"
            search_interval = 0

            [[autograb]]
            name = "Trying"
            type = "new"
            dry_run = true

            [[autograb]]
            name = "Real"
            type = "new"
            """);
        await fixture.CreateClient().GetStringAsync("/Jobs");

        Assert.Null(fixture.JobRegistry.Get("Trying")!.DryRunAsync);
        Assert.Contains("every run is a dry run", fixture.JobRegistry.Get("Trying")!.Description);
        Assert.NotNull(fixture.JobRegistry.Get("Real")!.DryRunAsync);
    }

    [Fact]
    public async Task History_ShowsARunsDetails_LinkingMamIdsToTheSearchPage()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetStringAsync("/Jobs");
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            db.JobRuns.Add(new JobRunEntity
            {
                JobName = "Upgrade",
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
                Result = JobRunResult.Success,
                Trigger = JobTrigger.DryRun,
                Summary = "Dry run: 1 would have been selected",
                DetailsJson = """["Would select: The Way of Kings by Brandon Sanderson (m4b, 1.2 GiB) - MAM 123456", "Skipped, not free: Other"]""",
            });
            await db.SaveChangesAsync();
        }

        var html = await client.GetStringAsync("/Jobs/History?job=Upgrade");

        Assert.Contains("<td class=\"nowrap\">Dry run</td>", html);
        Assert.Contains("<summary>2 lines</summary>", html);
        Assert.Contains("Would select: The Way of Kings by Brandon Sanderson (m4b, 1.2 GiB) - <a href=\"/search/123456\">MAM 123456</a>", html);
        Assert.Contains("<li>Skipped, not free: Other</li>", html);
    }

    [GeneratedRegex(@"<tr[\s\S]*?</tr>")]
    private static partial Regex RowPattern();

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""")]
    private static partial Regex AntiforgeryToken();
}
