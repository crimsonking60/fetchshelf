using System.Globalization;
using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Errors;

/// <summary>The Errors page: the jobs that are failing, then one row per item a job couldn't handle.</summary>
public class IndexModel(FetchshelfDbContext dbContext, IJobRegistry jobRegistry) : PageModel
{
    /// <summary>Only this stage's rows; null for all.</summary>
    [BindProperty(SupportsGet = true)]
    public ErrorStage? Stage { get; set; }

    /// <summary>"Job" (the default: the jobs in the order of their work, newest first within one) or "LastFailed".</summary>
    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "Job";

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = SortState.Ascending;

    public SortState SortState => SortState.From(Sort, Dir);

    /// <summary>The filter as route values, for the headings' links.</summary>
    public Dictionary<string, string> RouteValues =>
        Stage is { } stage ? new() { [nameof(Stage)] = stage.ToString() } : [];

    public IReadOnlyList<ErroredTorrentViewModel> Errors { get; private set; } = [];

    /// <summary>The failing jobs, one line each, or one line for several that the same MAM refusal stopped.</summary>
    public IReadOnlyList<FailingJobGroup> FailingJobs { get; private set; } = [];

    /// <summary>Whether there are rows at all, filtered out or not.</summary>
    public bool HasAnyRows { get; private set; }

    private static readonly ErrorStage[] HashKeyedStages = [ErrorStage.Linker, ErrorStage.Cleaner, ErrorStage.Audiobookshelf, ErrorStage.Upgrade];

    public async Task OnGetAsync()
    {
        FailingJobs = FailingJobGroups.Group(
            jobRegistry.GetAll().Where(j => j.Failing is not null).Select(j => (j.Name, j.Failing!)));

        var all = await dbContext.ErroredTorrents.ToListAsync();
        HasAnyRows = all.Count > 0;
        var entities = all.Where(e => Stage is null || e.Stage == Stage).ToList();
        var hashes = entities.Where(e => HashKeyedStages.Contains(e.Stage)).Select(e => e.Key).ToList();
        var trackedTitles = await dbContext.Torrents
            .Where(t => hashes.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title);
        var mamIds = entities
            .Where(e => e.Stage == ErrorStage.Snatchlist)
            .Select(e => long.TryParse(e.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .ToList();
        var torrentIdsByMamId = (await dbContext.Torrents
                .Where(t => mamIds.Contains(t.MamId))
                .Select(t => new { t.MamId, t.Id })
                .ToListAsync())
            .GroupBy(t => t.MamId.ToString(CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var newestFirst = entities.OrderByDescending(e => e.OccurredAt);
        var ordered = Sort == "LastFailed" ? SortState.Apply(entities, e => e.OccurredAt) : SortState.Apply(newestFirst, e => e.Stage);
        Errors = ordered
            .Select(e => ErroredTorrentViewModel.From(e, trackedTitles, torrentIdsByMamId))
            .ToList();
    }

    /// <summary>"Run now" for a failing job, as on the Jobs page; the result shows on the Jobs page once it has run.</summary>
    public IActionResult OnPostTrigger(string jobName)
    {
        if (jobRegistry.Get(jobName) is not null)
        {
            _ = jobRegistry.TriggerAsync(jobName, JobTrigger.Manual, CancellationToken.None);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveSelectedAsync(List<int> selectedIds)
    {
        await dbContext.ErroredTorrents.Where(e => selectedIds.Contains(e.Id)).ExecuteDeleteAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        var entity = await dbContext.ErroredTorrents.FindAsync(id);
        if (entity is not null && entity.Stage == ErrorStage.Grabber && entity.DisplayMetadataJson is not null)
        {
            var payload = JsonSerializer.Deserialize<GrabberErrorRestorePayload>(entity.DisplayMetadataJson)
                ?? throw new InvalidOperationException("Grabber error restore payload could not be deserialized.");

            dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
            {
                MamId = payload.MamId,
                DownloadLink = payload.DownloadLink,
                CostDecision = payload.CostDecision,
                Category = payload.Category,
                ClientName = payload.ClientName,
                SavePath = payload.SavePath,
                TagsJson = payload.TagsJson,
                GrabberName = payload.GrabberName,
                MetadataJson = payload.MetadataJson,
                // Recomputed rather than restored: the stored key may predate a change to the title rule.
                NormalizedTitleKey = TitleNormalizer.Normalize(TorrentMetadataSerializer.Deserialize(payload.MetadataJson).Title),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.ErroredTorrents.Remove(entity);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
