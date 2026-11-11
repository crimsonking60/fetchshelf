using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.ViewComponents;

/// <summary>What the badge on the navigation's Errors link counts.</summary>
/// <param name="FailingJobs">Jobs whose runs keep failing.</param>
/// <param name="NewErrors">Error rows recorded or updated in the last day.</param>
public sealed record ErrorBadge(int FailingJobs, int NewErrors)
{
    public int Total => FailingJobs + NewErrors;

    /// <summary>"1 job failing, 2 new errors", for the badge's tooltip.</summary>
    public string Description => string.Join(", ", new[]
    {
        FailingJobs == 0 ? null : $"{FailingJobs} {(FailingJobs == 1 ? "job" : "jobs")} failing",
        NewErrors == 0 ? null : $"{NewErrors} new {(NewErrors == 1 ? "error" : "errors")}",
    }.OfType<string>());
}

/// <summary>
/// The count on the navigation's Errors link: failing jobs, and error rows from the last day. Older rows stay on the
/// Errors page but don't keep the badge lit; it is for what changed. Failing jobs come from the registry, which holds
/// them in memory, so the only cost is one small query.
/// </summary>
public sealed class ErrorBadgeViewComponent(IJobRegistry jobRegistry, FetchshelfDbContext dbContext, TimeProvider timeProvider) : ViewComponent
{
    /// <summary>How long a new or updated error row keeps the badge lit.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(1);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var since = timeProvider.GetUtcNow() - NewFor;
        // SQLite can't compare DateTimeOffset values in a query; the table holds one row per failing item, so it is small.
        var occurred = await dbContext.ErroredTorrents.AsNoTracking().Select(e => e.OccurredAt).ToListAsync();
        var badge = new ErrorBadge(jobRegistry.GetAll().Count(j => j.Failing is not null), occurred.Count(at => at > since));
        return View(badge);
    }
}
