using System.Globalization;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Events;

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

    /// <summary>The names of the jobs that grabbed torrents, as events recorded them, for the Grabber dropdown.</summary>
    public IReadOnlyList<string> GrabberOptions { get; private set; } = [];

    /// <summary>The names of the libraries that linked or cleaned torrents, as events recorded them, for the Linker dropdown.</summary>
    public IReadOnlyList<string> LinkerOptions { get; private set; } = [];

    /// <summary>The fields an Updated event can record, for the Field dropdown.</summary>
    public static IReadOnlyList<string> FieldOptions => MetadataDiff.Fields;

    /// <summary>Whether the Field filter is offered and applied: it narrows Updated events, the only ones with changed fields.</summary>
    public bool FiltersByField => Type == EventKind.Updated;

    /// <summary>Whether any filter is set, so the page offers to clear them.</summary>
    public bool HasFilters => Type is not null || !string.IsNullOrWhiteSpace(Grabber) || !string.IsNullOrWhiteSpace(Linker)
        || (FiltersByField && !string.IsNullOrWhiteSpace(Field));

    /// <summary>The route values without any filter: the page size stays.</summary>
    public Dictionary<string, string> ClearedRouteValues() => new() { [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture) };

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Events/Index", FilterRouteValues(), PageNumber, TotalPages, Events.Count, TotalCount, PageSize, "event-pager", "Event pages");

    /// <summary>The filters and page size as route values, leaving out empty ones; paging is left out, so a new filter starts at page 1.</summary>
    public Dictionary<string, string> FilterRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Type)] = Type?.ToString() ?? string.Empty,
            [nameof(Grabber)] = Grabber ?? string.Empty,
            [nameof(Linker)] = Linker ?? string.Empty,
            [nameof(Field)] = FiltersByField ? Field ?? string.Empty : string.Empty,
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static async Task<List<string>> DistinctNamesAsync(IQueryable<string?> names) =>
        (await names.Where(name => name != null && name != "").Distinct().ToListAsync())
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The options, with the current value added when no event has it any more, so the dropdown still shows it.</summary>
    private static List<string> WithCurrent(List<string> options, string? current) =>
        string.IsNullOrWhiteSpace(current) || options.Contains(current, StringComparer.Ordinal) ? options : [current, .. options];

    private IReadOnlyDictionary<string, string> _titles = new Dictionary<string, string>();

    /// <summary>The torrent's title for an event's link text, falling back to its id when the torrent is no longer tracked.</summary>
    public string TorrentLabel(EventEntity e) =>
        e.TorrentId is { } id && _titles.TryGetValue(id, out var title) ? title : e.TorrentId ?? string.Empty;

    public async Task OnGetAsync()
    {
        PageSize = PageSizeCookie.Apply(HttpContext, "events", PageSize);
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

        if (FiltersByField && !string.IsNullOrWhiteSpace(Field))
        {
            // The field's name as the stored change list writes it, so a value that happens to hold the word doesn't match.
            var fieldJson = $"\"Field\":{System.Text.Json.JsonSerializer.Serialize(Field)}";
            query = query.Where(e => e.FieldChangesJson != null && e.FieldChangesJson.Contains(fieldJson));
        }

        GrabberOptions = WithCurrent(await DistinctNamesAsync(dbContext.Events.Select(e => e.GrabberName)), Grabber);
        LinkerOptions = WithCurrent(await DistinctNamesAsync(dbContext.Events.Select(e => e.LinkerName)), Linker);

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
