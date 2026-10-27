using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Errors;

/// <summary>The Errors page, Fetchshelf-spec.md §10.4: per-stage error list.</summary>
public class IndexModel(FetchshelfDbContext dbContext) : PageModel
{
    public IReadOnlyList<ErroredTorrentViewModel> Errors { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var entities = await dbContext.ErroredTorrents.ToListAsync();
        Errors = entities
            .OrderBy(e => e.Stage)
            .ThenByDescending(e => e.OccurredAt)
            .Select(ErroredTorrentViewModel.From)
            .ToList();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        var entity = await dbContext.ErroredTorrents.FindAsync(id);
        if (entity is not null)
        {
            dbContext.ErroredTorrents.Remove(entity);
            await dbContext.SaveChangesAsync();
        }

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
                TagsJson = payload.TagsJson,
                UnsatBuffer = payload.UnsatBuffer,
                WedgeBuffer = payload.WedgeBuffer,
                GrabberName = payload.GrabberName,
                MetadataJson = payload.MetadataJson,
                NormalizedTitleKey = payload.NormalizedTitleKey,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.ErroredTorrents.Remove(entity);
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
