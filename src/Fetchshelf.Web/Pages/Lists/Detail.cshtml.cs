using System.Globalization;
using System.Text.Json;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Lists;

/// <summary>
/// One list's books in the list's own order, with what was found for each format, a filter for books still missing
/// a format, and Mark done / Undo.
/// </summary>
public class DetailModel(FetchshelfDbContext dbContext) : PageModel
{
    /// <summary>"any", "audio" or "ebook" to show only books still missing that format; empty for all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }

    /// <summary>Also show books that are no longer on the list at Hardcover.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Removed { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public string ListId { get; private set; } = string.Empty;

    public string ListName { get; private set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public IReadOnlyList<ListItemViewModel> Items { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Lists/Detail", RouteValues(), PageNumber, TotalPages, Items.Count, TotalCount, PageSize, "list-pager", "List pages");

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var list = await dbContext.Lists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        if (list is null)
        {
            return NotFound();
        }

        (ListId, ListName, UpdatedAt) = (list.Id, list.Name, list.UpdatedAt);
        PageSize = PageSizeCookie.Apply(HttpContext, "lists", PageSize);

        var entities = await dbContext.ListItems.AsNoTracking().Where(i => i.ListId == id).ToListAsync();
        var shown = entities
            .Where(i => Removed || i.RemovedAt is null)
            .Where(i => Show switch
            {
                "any" => Missing(i.AllowAudio, i.AudioStatus, i) || Missing(i.AllowEbook, i.EbookStatus, i),
                "audio" => Missing(i.AllowAudio, i.AudioStatus, i),
                "ebook" => Missing(i.AllowEbook, i.EbookStatus, i),
                _ => true,
            })
            .OrderBy(i => i.RemovedAt is not null)
            .ThenBy(i => i.Position)
            .ToList();

        TotalCount = shown.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(shown, TotalCount, PageNumber, PageSize);
        var torrentIds = await TorrentIdsByMamIdAsync(page);
        Items = page.Select(i => ToViewModel(i, torrentIds)).ToList();
        return Page();
    }

    public Task<IActionResult> OnPostMarkDoneAsync(string id, string externalId) => SetDoneAsync(id, externalId, DateTimeOffset.UtcNow);

    public Task<IActionResult> OnPostUndoDoneAsync(string id, string externalId) => SetDoneAsync(id, externalId, null);

    private async Task<IActionResult> SetDoneAsync(string id, string externalId, DateTimeOffset? doneAt)
    {
        var item = await dbContext.ListItems.FindAsync(id, externalId);
        if (item is null)
        {
            return NotFound();
        }

        item.MarkedDoneAt = doneAt;
        await dbContext.SaveChangesAsync();
        var values = RouteValues();
        values["id"] = id;
        values[nameof(PageNumber)] = PageNumber.ToString(CultureInfo.InvariantCulture);
        return RedirectToPage(values.ToDictionary(v => v.Key, v => (object?)v.Value));
    }

    /// <summary>The filters and page size, for links and redirects that should keep them.</summary>
    public Dictionary<string, string> RouteValues()
    {
        var values = new Dictionary<string, string> { ["id"] = ListId, [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrEmpty(Show))
        {
            values[nameof(Show)] = Show;
        }

        if (Removed)
        {
            values[nameof(Removed)] = "true";
        }

        return values;
    }

    private static bool Missing(bool allowed, string? status, ListItemEntity item) =>
        allowed && item.MarkedDoneAt is null && !ListItemSideStatus.IsResolved(status);

    private async Task<Dictionary<long, string>> TorrentIdsByMamIdAsync(IEnumerable<ListItemEntity> items)
    {
        var mamIds = items.SelectMany(i => new[] { i.AudioMamId, i.EbookMamId }).OfType<long>().Distinct().ToList();
        var torrents = await dbContext.Torrents.AsNoTracking()
            .Where(t => mamIds.Contains(t.MamId))
            .Select(t => new { t.MamId, t.Id })
            .ToListAsync();
        return torrents.GroupBy(t => t.MamId).ToDictionary(g => g.Key, g => g.First().Id);
    }

    private static ListItemViewModel ToViewModel(ListItemEntity entity, Dictionary<long, string> torrentIds)
    {
        var authors = JsonSerializer.Deserialize<List<string>>(entity.AuthorsJson) ?? [];
        return new ListItemViewModel
        {
            ExternalId = entity.ExternalId,
            Title = entity.BookTitle,
            Authors = authors,
            SeriesText = entity.SeriesName is null
                ? null
                : entity.SeriesNumber is { } number ? $"{entity.SeriesName} #{number.ToString(CultureInfo.InvariantCulture)}" : entity.SeriesName,
            CoverUrl = entity.CoverUrl,
            BookUrl = entity.BookUrl,
            MamSearchUrl = $"/?Q={Uri.EscapeDataString(ListMamQueryBuilder.Build(entity.BookTitle, authors))}",
            Audio = entity.AllowAudio ? Side(entity.AudioStatus, entity.AudioMamId, "audiobook", torrentIds) : null,
            Ebook = entity.AllowEbook ? Side(entity.EbookStatus, entity.EbookMamId, "ebook", torrentIds) : null,
            MarkedDone = entity.MarkedDoneAt is not null,
            Removed = entity.RemovedAt is not null,
        };
    }

    private static ListItemSideViewModel Side(string? status, long? mamId, string format, Dictionary<long, string> torrentIds)
    {
        var torrentLink = mamId is { } id && torrentIds.TryGetValue(id, out var torrentId) ? $"/torrents/{Uri.EscapeDataString(torrentId)}" : null;
        return status switch
        {
            null => new("Not searched for yet", null),
            ListItemSideStatus.Existing => new("In the library", torrentLink),
            ListItemSideStatus.Selected => torrentLink is null ? new("Selected for download", "/Selected") : new("In the library", torrentLink),
            ListItemSideStatus.Wanted => new("On MAM, but it costs more than the list's grab blocks allow", null),
            ListItemSideStatus.NotWanted => new($"Not wanted: the list prefers the other format to the {format}", null),
            ListItemSideStatus.NotFound => new("Not found on MAM", null),
            _ => new(status, null),
        };
    }
}
