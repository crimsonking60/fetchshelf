using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Jobs;

public class IndexModel(IJobRegistry jobRegistry, FetchshelfSettings settings) : PageModel
{
    public IReadOnlyList<JobStatusViewModel> Jobs { get; private set; } = [];

    /// <summary>Kinds of optional job with nothing configured, listed below the jobs.</summary>
    public IReadOnlyList<string> Unconfigured { get; private set; } = [];

    public void OnGet()
    {
        Jobs = BuildJobViewModels();
        Unconfigured = UnconfiguredJobs.For(settings);
    }

    /// <summary>Polled by htmx (see `pollJobs` in Index.cshtml, currently off) to refresh the job list without a full page reload.</summary>
    public PartialViewResult OnGetJobsPartial()
    {
        Jobs = BuildJobViewModels();
        return Partial("_JobListPartial", Jobs);
    }

    /// <summary>
    /// The "Run now" action for a registered job. Starts the job without waiting for it to finish, so the
    /// response reflects "Running" immediately; polling (see `pollJobs` in Index.cshtml) picks up the
    /// completed result once the job finishes. <see cref="CancellationToken.None"/> is used deliberately
    /// instead of the request's token, since the job must outlive this request.
    /// </summary>
    public PartialViewResult OnPostTrigger(string jobName)
    {
        _ = jobRegistry.TriggerAsync(jobName, JobTrigger.Manual, CancellationToken.None);
        Jobs = BuildJobViewModels();
        return Partial("_JobListPartial", Jobs);
    }

    /// <summary>
    /// The "Cancel" action for a running job. Asks the run to stop without waiting for it: each job stops at the next
    /// point where nothing is left half done, which polling then shows.
    /// </summary>
    public PartialViewResult OnPostCancel(string jobName)
    {
        jobRegistry.Cancel(jobName);
        Jobs = BuildJobViewModels();
        return Partial("_JobListPartial", Jobs);
    }

    private static readonly string[] PriorityJobOrder = ["Download", "Link", "Clean"];

    private IReadOnlyList<JobStatusViewModel> BuildJobViewModels() =>
        jobRegistry.GetAll()
            .Select(JobStatusViewModel.From)
            .OrderBy(j => Array.IndexOf(PriorityJobOrder, j.Name) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(j => j.Name, StringComparer.Ordinal)
            .ToList();
}
