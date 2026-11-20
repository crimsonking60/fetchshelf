using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Web.Tests;

/// <summary>Each Upgrade run leaves an event with its result as the Jobs page shows it, which the Events page lists.</summary>
public class UpgradeRunEventsTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static JobRunFinished Finished(string job, JobRunResult result, string summary) =>
        new(job, new JobRunRecord(Started, result, summary, Started.AddMinutes(2)), null, null, null);

    private static UpgradeRunEvents Listener(FetchshelfWebAppFixture fixture) =>
        new(fixture.CreateScope().ServiceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<UpgradeRunEvents>.Instance);

    private static List<EventEntity> Events(FetchshelfWebAppFixture fixture) =>
        fixture.CreateScope().ServiceProvider.GetRequiredService<FetchshelfDbContext>().Events.AsNoTracking().ToList();

    [Fact]
    public async Task AnUpgradeRun_IsAnEventWithTheJobsPagesResult()
    {
        using var fixture = new FetchshelfWebAppFixture();

        await Listener(fixture).WriteAsync(Finished(JobNames.Upgrade, JobRunResult.Success, "3 books searched (A, B, C), 1 better copy selected"));

        var upgrade = Assert.Single(Events(fixture));
        Assert.Equal(EventKind.UpgradeRun, upgrade.Kind);
        Assert.Equal("Success: 3 books searched (A, B, C), 1 better copy selected", upgrade.RunSummary);
        Assert.Equal(Started.AddMinutes(2), upgrade.OccurredAt);
        Assert.Null(upgrade.TorrentId);
    }

    [Fact]
    public async Task OtherJobsRuns_LeaveNoEvent()
    {
        using var fixture = new FetchshelfWebAppFixture();

        Listener(fixture).RunFinished(Finished(JobNames.Link, JobRunResult.Success, "Linked 2"));
        await Task.Delay(200);

        Assert.Empty(Events(fixture));
    }

    [Fact]
    public async Task TheListener_WritesAfterTheRun_WithoutHoldingTheJobUp()
    {
        using var fixture = new FetchshelfWebAppFixture();

        Listener(fixture).RunFinished(Finished(JobNames.Upgrade, JobRunResult.CompletedWithErrors, "1 book searched (A), 0 better copies selected; 1 could not be searched for"));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Events(fixture).Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.StartsWith("Finished with errors: 1 book searched", Assert.Single(Events(fixture)).RunSummary);
    }

    [Fact]
    public async Task EventsPage_ListsTheRun_AsAnUpgradeRun_WithWhatItDid()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await Listener(fixture).WriteAsync(Finished(JobNames.Upgrade, JobRunResult.Success, "2 books searched (A, B), 0 better copies selected"));

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/events"));

        Assert.Contains("Upgrade run", html);
        Assert.Contains("Success: 2 books searched (A, B), 0 better copies selected", html);
    }

    [Fact]
    public void TheApp_RegistersTheListener_WithTheRegistry()
    {
        using var fixture = new FetchshelfWebAppFixture();

        Assert.Contains(fixture.CreateScope().ServiceProvider.GetServices<IJobRunListener>(), listener => listener is UpgradeRunEvents);
    }
}
