using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.ViewComponents;

/// <summary>What the badge on the navigation's Errors entry counts.</summary>
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
/// The main navigation, on every page: <see cref="NavModel"/> built from the configuration and the counts behind the
/// badges. The Errors count is failing jobs plus error rows from the last day (older rows stay on the Errors page
/// but don't keep the badge lit; it is for what changed). Failing jobs come from the registry, which holds them in
/// memory, so the cost on every page is two counts.
/// </summary>
public sealed class MainNavViewComponent(
    IJobRegistry jobRegistry, FetchshelfDbContext dbContext, TimeProvider timeProvider, FetchshelfSettings settings, LibraryCheckResults libraryCheck)
    : ViewComponent
{
    /// <summary>How long a new or updated error row keeps the Errors badge lit.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(1);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var since = (timeProvider.GetUtcNow() - NewFor).ToUniversalTime();
        // EF can't compare DateTimeOffset values in a SQLite query, but SQLite holds them as text that sorts as the times
        // do while they share an offset. Error rows are always written in UTC (ErrorLog), so the cutoff, bound in UTC in
        // the same form, is compared by SQLite, and the rows, one per book of a long list at worst, are never loaded.
        var newErrors = await dbContext.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"ErroredTorrents\" WHERE \"OccurredAt\" > {since}")
            .SingleAsync();
        var errors = new ErrorBadge(jobRegistry.GetAll().Count(j => j.Failing is not null), newErrors);
        var followCount = settings.Follow is { } follow
            ? await FollowQueries.WaitingCountAsync(dbContext, FollowQueries.WantedSides(follow.Grab))
            : 0;

        var model = NavModel.Build(new NavInputs(
            HasAccount: settings.BackgroundJobsEnabled,
            FollowConfigured: settings.Follow is not null,
            ListsConfigured: settings.HardcoverList.Count > 0 || settings.FileList.Count > 0 || settings.BookmarksList.Count > 0,
            ErrorCount: errors.Total,
            ErrorDescription: errors.Description,
            FollowCount: followCount,
            LibraryFindings: libraryCheck.Findings.Count));
        return View(model);
    }
}
