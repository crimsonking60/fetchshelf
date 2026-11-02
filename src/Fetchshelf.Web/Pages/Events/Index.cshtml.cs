using System.Globalization;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Events;

/// <summary>The Events page.</summary>
public class IndexModel(FetchshelfDbContext dbContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public EventKind? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Grabber { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Linker { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Field { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public IReadOnlyList<EventEntity> Events { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Events/Index", FilterRouteValues(), PageNumber, TotalPages, Events.Count, TotalCount, "event-pager", "Event pages");

    /// <summary>The filters and page size as route values, leaving out empty ones; paging is left out, so a new filter starts at page 1.</summary>
    public Dictionary<string, string> FilterRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Type)] = Type?.ToString() ?? string.Empty,
            [nameof(Grabber)] = Grabber ?? string.Empty,
            [nameof(Linker)] = Linker ?? string.Empty,
            [nameof(Field)] = Field ?? string.Empty,
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private IReadOnlyDictionary<string, string> _titles = new Dictionary<string, string>();

    /// <summary>The torrent's title for an event's link text, falling back to its id when the torrent is no longer tracked.</summary>
    public string TorrentLabel(EventEntity e) =>
        e.TorrentId is { } id && _titles.TryGetValue(id, out var title) ? title : e.TorrentId ?? string.Empty;

    public async Task OnGetAsync()
    {
        var query = dbContext.Events.AsNoTracking().AsQueryable();

        if (Type is { } type)
        {
            query = query.Where(e => e.Kind == type);
        }

        if (!string.IsNullOrWhiteSpace(Grabber))
        {
            query = query.Where(e => e.GrabberName == Grabber);
        }

        if (!string.IsNullOrWhiteSpace(Linker))
        {
            query = query.Where(e => e.LinkerName == Linker);
        }

        if (!string.IsNullOrWhiteSpace(Field))
        {
            query = query.Where(e => e.FieldChangesJson != null && e.FieldChangesJson.Contains(Field));
        }

        // Sorted and paged in memory: the SQLite EF Core provider cannot translate ORDER BY over a
        // DateTimeOffset column (see the same note in Pages/Torrents/Index.cshtml.cs).
        var matched = await query.ToListAsync();
        TotalCount = matched.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(matched.OrderByDescending(e => e.OccurredAt), TotalCount, PageNumber, PageSize);
        Events = page;

        var torrentIds = page.Where(e => e.TorrentId != null).Select(e => e.TorrentId!).Distinct().ToList();
        _titles = await dbContext.Torrents.AsNoTracking()
            .Where(t => torrentIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title);
    }
}
