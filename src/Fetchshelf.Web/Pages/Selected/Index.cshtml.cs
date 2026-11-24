using System.Text.Json;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Selected;

/// <summary>The Selected page: the download queue.</summary>
public class IndexModel(FetchshelfDbContext dbContext, MamClientOptions mamOptions, FetchshelfSettings settings) : PageModel
{
    /// <summary>"CreatedAt" (the default, oldest first: the order the Download job starts them in), "Title" or "Cost".</summary>
    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "CreatedAt";

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = SortState.Ascending;

    public SortState SortState => SortState.From(Sort, Dir);

    public IReadOnlyList<SelectedTorrentViewModel> Selections { get; private set; } = [];

    /// <summary>MAM's address, for the link each title carries to its torrent there.</summary>
    public string MamBaseUrl => mamOptions.BaseUrl;

    public async Task OnGetAsync()
    {
        var entities = await dbContext.SelectedTorrents.Where(s => s.RemovedAt == null).ToListAsync();
        var rows = entities.Select(SelectedTorrentViewModel.From).ToList();
        var oldestFirst = rows.OrderBy(r => r.CreatedAt);
        Selections = (Sort switch
        {
            "Title" => SortState.Apply(oldestFirst, r => r.Title),
            "Cost" => SortState.Apply(oldestFirst, r => r.CostDecision),
            _ => SortState.Apply(rows, r => r.CreatedAt),
        }).ToList();
    }

    /// <summary>
    /// One selection's details, for the dialog its title opens: what was stored when it was picked, with a link to its
    /// MAM page. Nothing is asked of MAM.
    /// </summary>
    public async Task<IActionResult> OnGetDetailsAsync(int id)
    {
        var entity = await dbContext.SelectedTorrents.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return entity is null
            ? NotFound()
            : Partial("_SelectionDetailsPartial", SelectionDetailsViewModel.From(entity, mamOptions.BaseUrl, settings.Genres));
    }

    /// <summary>Takes the ticked selections out of the queue; ones already removed keep their original removal time.</summary>
    public async Task<IActionResult> OnPostRemoveSelectedAsync(List<int> selectedIds)
    {
        var entities = await dbContext.SelectedTorrents
            .Where(s => selectedIds.Contains(s.Id) && s.RemovedAt == null)
            .ToListAsync();
        var removedAt = DateTimeOffset.UtcNow;
        foreach (var entity in entities)
        {
            entity.RemovedAt = removedAt;
        }

        await dbContext.SaveChangesAsync();
        return RedirectToPage();
    }

    /// <summary>The costs a selection can be changed to, as stored (see <see cref="SelectedTorrentCosts.ChangeableByHand"/>).</summary>
    public static readonly IReadOnlyList<string> ChangeableCosts =
        SelectedTorrentCosts.ChangeableByHand.Select(c => c.ToString()).ToList();

    /// <summary>The cost choices for one selection: its current cost first, then the changeable ones.</summary>
    public static IEnumerable<string> CostChoicesFor(SelectedTorrentViewModel selection) =>
        ChangeableCosts.Contains(selection.CostDecision) ? ChangeableCosts : [selection.CostDecision, .. ChangeableCosts];

    /// <summary>
    /// Saves the category, tags (and cost, if given) of a selection that is still waiting to download. Once a selection
    /// has started its category and tags have already gone to qBittorrent and nothing reads them again, and its cost
    /// feeds the in-flight budget, so a started or removed selection is left as it is.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateAsync(int id, string? category, string? tags, string? cost)
    {
        var entity = await dbContext.SelectedTorrents.FindAsync(id);
        if (entity is { StartedAt: null, RemovedAt: null })
        {
            if (!string.IsNullOrWhiteSpace(cost) && cost != entity.CostDecision && !ChangeableCosts.Contains(cost))
            {
                return BadRequest();
            }

            entity.Category = string.IsNullOrWhiteSpace(category) ? null : category;
            entity.TagsJson = JsonSerializer.Serialize(
                (tags ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrWhiteSpace(cost))
            {
                entity.CostDecision = cost;
            }

            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
