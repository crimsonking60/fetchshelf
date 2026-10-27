using System.Text.Json;
using Fetchshelf.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Selected;

/// <summary>The Selected page, Fetchshelf-spec.md §10.4: the download queue.</summary>
public class IndexModel(FetchshelfDbContext dbContext) : PageModel
{
    public IReadOnlyList<SelectedTorrentViewModel> Selections { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var entities = await dbContext.SelectedTorrents.Where(s => s.RemovedAt == null).ToListAsync();
        Selections = entities
            .OrderByDescending(e => e.CreatedAt)
            .Select(SelectedTorrentViewModel.From)
            .ToList();
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

    public async Task<IActionResult> OnPostUpdateAsync(int id, string? category, string? tags, string? cost)
    {
        var entity = await dbContext.SelectedTorrents.FindAsync(id);
        if (entity is not null)
        {
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
