using System.Globalization;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>One job's runs, newest first.</summary>
public class HistoryModel(FetchshelfDbContext dbContext) : PageModel
{
    public const int DefaultPageSize = 50;

    [BindProperty(SupportsGet = true)]
    public string? Job { get; set; }

    /// <summary>Only the runs that didn't succeed: errors, runs finished with errors, cancelled and interrupted ones.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Unsuccessful { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    public IReadOnlyList<JobRunEntity> Runs { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Jobs/History", FilterRouteValues(), PageNumber, TotalPages, Runs.Count, TotalCount, PageSize, "job-run-pager", "Job run pages");

    public Dictionary<string, string> FilterRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Job)] = Job ?? string.Empty,
            [nameof(Unsuccessful)] = Unsuccessful ? "true" : string.Empty,
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public async Task OnGetAsync()
    {
        PageSize = PageSizeCookie.Apply(HttpContext, "job-runs", PageSize);
        var query = dbContext.JobRuns.AsNoTracking().Where(r => r.JobName == Job);
        if (Unsuccessful)
        {
            query = query.Where(r => r.Result != JobRunResult.Success && r.Result != JobRunResult.Running);
        }

        // Ids grow with every run, so the highest id is the newest run; SQLite can't sort on the DateTimeOffset column.
        var runs = await query.OrderByDescending(r => r.Id).ToListAsync();
        TotalCount = runs.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(runs, TotalCount, PageNumber, PageSize);
        Runs = page;
    }

    public static string? Duration(JobRunEntity run) =>
        run.FinishedAt is { } finishedAt ? JobRunLabels.Duration(finishedAt - run.StartedAt) : null;

    public static string? Counts(JobRunEntity run) => JobRunLabels.Counts(JobRunStore.ReadCounts(run.CountsJson));
}
