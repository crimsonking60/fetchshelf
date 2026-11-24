using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Jobs;

public class IndexModel(
    IJobRegistry jobRegistry,
    FetchshelfSettings settings,
    Account.AccountSummaryBuilder accountSummaryBuilder,
    BulkOperationQueue bulkOperations,
    LibraryCheckResults libraryCheck)
    : PageModel
{
    /// <summary>The jobs in their groups, and the kinds of optional job with nothing configured.</summary>
    public JobListViewModel JobList { get; private set; } = new([], []);

    /// <summary>The Torrents page's bulk actions running or waiting: not jobs, but work going on all the same.</summary>
    public IReadOnlyList<BulkOperation> BulkOperations { get; private set; } = [];

    /// <summary>The account numbers above the job list; null without a `mam_id`, when there is no account to show.</summary>
    public Account.AccountSummary? Account { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        JobList = BuildJobList();
        BulkOperations = bulkOperations.Active();
        Account = settings.BackgroundJobsEnabled ? await accountSummaryBuilder.BuildStripAsync(cancellationToken) : null;
    }

    /// <summary>The names of the jobs running now, for the status pill in the menu bar (run-status.js polls this).</summary>
    public JsonResult OnGetRunning() =>
        new(jobRegistry.GetAll().Where(job => job.IsRunning).Select(job => job.Name).Order(StringComparer.OrdinalIgnoreCase).ToList());

    /// <summary>
    /// Polled by htmx (see `pollJobs` in Index.cshtml) to refresh the job list without a full page reload. The poll sends
    /// the <see cref="JobListVersion"/> of the list it shows; while that is still the list's version, the answer is 204
    /// No Content, which htmx leaves unswapped, so the list on screen is only replaced when a job started, ended or moved.
    /// </summary>
    public IActionResult OnGetJobsPartial(string? version)
    {
        JobList = BuildJobList();
        return version is not null && version == JobList.Version ? StatusCode(StatusCodes.Status204NoContent) : Partial("_JobListPartial", JobList);
    }

    /// <summary>
    /// The "Run now" action for a registered job. Starts the job without waiting for it to finish, so the
    /// response reflects "Running" immediately; polling (see `pollJobs` in Index.cshtml) picks up the
    /// completed result once the job finishes. <see cref="CancellationToken.None"/> is used deliberately
    /// instead of the request's token, since the job must outlive this request. A plain form, such as the Config page's
    /// <b>Run now</b>, is sent on to the Jobs page instead of getting the bare list.
    /// </summary>
    public IActionResult OnPostTrigger(string jobName, bool dryRun = false)
    {
        // A job without a dry run gets a real one only if asked for one: a Dry run button from a page out of date never runs it for real.
        if (!dryRun || (settings.DryJobButtons && jobRegistry.Get(jobName)?.DryRunAsync is not null))
        {
            _ = jobRegistry.TriggerAsync(jobName, dryRun ? JobTrigger.DryRun : JobTrigger.Manual, CancellationToken.None);
        }

        return JobListOrPage();
    }

    /// <summary>
    /// The "Cancel" action for a running job. Asks the run to stop without waiting for it: each job stops at the next
    /// point where nothing is left half done, which polling then shows. The button carries the number of the run it was
    /// shown for, so a click on a list a moment out of date doesn't stop a run that started since.
    /// </summary>
    public IActionResult OnPostCancel(string jobName, long? runNumber)
    {
        jobRegistry.Cancel(jobName, runNumber);
        return JobListOrPage();
    }

    /// <summary>The job list for htmx to swap in; the whole page for a plain form post.</summary>
    private IActionResult JobListOrPage()
    {
        if (!Request.Headers.ContainsKey("HX-Request"))
        {
            return RedirectToPage("/Jobs/Index");
        }

        JobList = BuildJobList();
        return Partial("_JobListPartial", JobList);
    }

    private JobListViewModel BuildJobList()
    {
        var list = JobGrouping.Build(jobRegistry.GetAll().Select(JobStatusViewModel.From), settings);
        return list with { Version = JobListVersion.Of(list, settings.JobExplanations, settings.DryJobButtons, libraryCheck.Findings.Count) };
    }
}
