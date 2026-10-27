using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Events;

/// <summary>The Events page, Fetchshelf-spec.md §3.3, §10.4.</summary>
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

    public IReadOnlyList<EventEntity> Events { get; private set; } = [];

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

        // Sorted in memory: the SQLite EF Core provider cannot translate ORDER BY over a
        // DateTimeOffset column (see the same note in Pages/Torrents/Index.cshtml.cs).
        var matched = await query.ToListAsync();
        Events = matched.OrderByDescending(e => e.OccurredAt).ToList();

        var torrentIds = query.Where(e => e.TorrentId != null).Select(e => e.TorrentId!);
        _titles = await dbContext.Torrents.AsNoTracking()
            .Where(t => torrentIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title);
    }
}
