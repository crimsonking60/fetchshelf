using Fetchshelf.Core.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>The Jobs page, Fetchshelf-spec.md §10.1.</summary>
public class IndexModel(IJobRegistry jobRegistry) : PageModel
{
    public IReadOnlyList<JobStatusViewModel> Jobs { get; private set; } = [];

    public void OnGet() => Jobs = BuildJobViewModels();

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
        _ = jobRegistry.TriggerAsync(jobName, CancellationToken.None);
        Jobs = BuildJobViewModels();
        return Partial("_JobListPartial", Jobs);
    }

    private IReadOnlyList<JobStatusViewModel> BuildJobViewModels() =>
        jobRegistry.GetAll().Select(JobStatusViewModel.From).ToList();
}
