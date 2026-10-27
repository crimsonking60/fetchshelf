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

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var entity = await dbContext.SelectedTorrents.FindAsync(id);
        if (entity is not null)
        {
            entity.RemovedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

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
