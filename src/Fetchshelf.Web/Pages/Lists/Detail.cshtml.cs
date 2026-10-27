using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Lists;

/// <summary>
/// The List detail page (`/lists/{id}`), Fetchshelf-spec.md §10.4, §7.8: per-book status, a show filter, and
/// a "mark done" action.
/// </summary>
public class DetailModel(FetchshelfDbContext dbContext) : PageModel
{
    private static readonly HashSet<string> ResolvedStatuses = ["Selected", "Existing", "NotWanted"];

    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    public string ListId { get; private set; } = string.Empty;

    public string ListName { get; private set; } = string.Empty;

    public IReadOnlyList<ListItemViewModel> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var list = await dbContext.Lists.FindAsync(id);
        if (list is null)
        {
            return NotFound();
        }

        ListId = id;
        ListName = list.Name;

        var entities = await dbContext.ListItems.Where(i => i.ListId == id).ToListAsync();
        var items = new List<ListItemViewModel>();
        foreach (var entity in entities)
        {
            items.Add(await ToViewModelAsync(entity));
        }

        Items = Show switch
        {
            "any" => items.Where(i => i.AudioMissing || i.EbookMissing).ToList(),
            "audio" => items.Where(i => i.AudioMissing).ToList(),
            "ebook" => items.Where(i => i.EbookMissing).ToList(),
            _ => items,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostMarkDoneAsync(string id, string guid)
    {
        var item = await dbContext.ListItems.FindAsync(id, guid);
        if (item is not null)
        {
            item.MarkedDoneAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        return RedirectToPage(new { id, show = Show });
    }

    private async Task<ListItemViewModel> ToViewModelAsync(ListItemEntity entity)
    {
        var authors = JsonSerializer.Deserialize<List<string>>(entity.AuthorsJson) ?? [];
        var (audioDisplay, audioLink) = await ResolveSideAsync(entity.AudioStatus, entity.AudioMamId);
        var (ebookDisplay, ebookLink) = await ResolveSideAsync(entity.EbookStatus, entity.EbookMamId);

        return new ListItemViewModel
        {
            Guid = entity.Guid,
            Title = entity.BookTitle,
            Authors = authors,
            SeriesText = entity.SeriesName is null ? null : $"{entity.SeriesName} #{entity.SeriesNumber}",
            CoverUrl = entity.CoverUrl,
            BookUrl = entity.BookUrl,
            MamSearchUrl = $"/?Q={Uri.EscapeDataString(ListMamQueryBuilder.Build(entity.BookTitle, authors))}",
            AudioAllowed = entity.AllowAudio,
            AudioDisplay = entity.AllowAudio ? audioDisplay : null,
            AudioLink = audioLink,
            AudioMissing = entity.AllowAudio && !IsResolved(entity.AudioStatus),
            EbookAllowed = entity.AllowEbook,
            EbookDisplay = entity.AllowEbook ? ebookDisplay : null,
            EbookLink = ebookLink,
            EbookMissing = entity.AllowEbook && !IsResolved(entity.EbookStatus),
            CanMarkDone = entity.MarkedDoneAt is null && (entity.AllowAudio || entity.AllowEbook)
                && (!IsResolved(entity.AudioStatus) || !IsResolved(entity.EbookStatus)),
        };
    }

    private async Task<(string Display, string? Link)> ResolveSideAsync(string? status, long? mamId)
    {
        switch (status)
        {
            case null:
                return ("Missing", null);
            case "Selected":
                return ("Selected" + (mamId is { } selectedMamId ? $" (torrent {selectedMamId})" : string.Empty), "/Selected");
            case "Existing":
                var torrent = mamId is { } existingMamId
                    ? await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == existingMamId)
                    : null;
                return ("Found in library", torrent is null ? null : $"/torrents/{torrent.Id}");
            case "Wanted":
                return ("Missing (rejected by cost policy)", null);
            case "NotWanted":
                return ("Not wanted", null);
            default:
                return (status, null);
        }
    }

    private static bool IsResolved(string? status) => status is not null && ResolvedStatuses.Contains(status);
}
