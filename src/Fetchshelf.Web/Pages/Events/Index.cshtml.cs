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

    /// <summary>"OccurredAt" (the default) or "Type".</summary>
    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "OccurredAt";

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = SortState.DescendingValue;

    public SortState SortState => SortState.From(Sort, Dir);

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
            [nameof(Sort)] = Sort,
            [nameof(Dir)] = Dir,
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

    private IReadOnlyDictionary<int, CleanedReplacement> _replacements = new Dictionary<int, CleanedReplacement>();

    /// <summary>The better copy a Cleaned event's torrent was retired in favour of, when it is still tracked.</summary>
    public CleanedReplacement? Replacement(EventEntity e) => _replacements.GetValueOrDefault(e.Id);

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
        // Rows with the same time or type are kept newest first, whichever way the list is sorted.
        var newestFirst = matched.OrderByDescending(e => e.OccurredAt);
        var ordered = Sort == "Type" ? SortState.Apply(newestFirst, e => EventLabels.For(e.Kind)) : SortState.Apply(matched, e => e.OccurredAt);
        (var page, PageNumber, TotalPages) = Paging.Slice(ordered, TotalCount, PageNumber, PageSize);
        Events = page;

        var torrentIds = page.Where(e => e.TorrentId != null).Select(e => e.TorrentId!).Distinct().ToList();
        var torrents = await dbContext.Torrents.AsNoTracking()
            .Where(t => torrentIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.ReplacedWithTorrentId })
            .ToDictionaryAsync(t => t.Id);
        _titles = torrents.ToDictionary(pair => pair.Key, pair => pair.Value.Title);

        // The better copy each Cleaned event names; events written before it was recorded take the one the retired
        // torrent is marked replaced with, as long as it still is.
        var replacedBy = page
            .Where(e => e.Kind == EventKind.Cleaned)
            .Select(e => (e.Id, Keeper: e.ReplacedByTorrentId
                ?? (e.TorrentId is { } id && torrents.TryGetValue(id, out var torrent) ? torrent.ReplacedWithTorrentId : null)))
            .Where(pair => pair.Keeper is not null)
            .ToList();
        var keeperIds = replacedBy.Select(pair => pair.Keeper!).Distinct().ToList();
        var keepers = await dbContext.Torrents.AsNoTracking()
            .Where(t => keeperIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.ChosenAudioExtension, t.ChosenEbookExtension })
            .ToDictionaryAsync(t => t.Id);
        _replacements = replacedBy.ToDictionary(
            pair => pair.Id,
            pair => keepers.TryGetValue(pair.Keeper!, out var keeper)
                ? new CleanedReplacement(keeper.Id, keeper.Title, string.Join(" and ", new[] { keeper.ChosenAudioExtension, keeper.ChosenEbookExtension }.OfType<string>()))
                : new CleanedReplacement(pair.Keeper!, pair.Keeper!, string.Empty));
    }
}

/// <summary>The better copy a Cleaned event's torrent was retired in favour of: its id, title and linked format.</summary>
public sealed record CleanedReplacement(string TorrentId, string Title, string Format);
