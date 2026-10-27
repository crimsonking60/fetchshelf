using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>The Torrents page, Fetchshelf-spec.md §10.2.</summary>
public class IndexModel(
    FetchshelfDbContext dbContext, LinkRunner linkRunner, Cleaner cleaner,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory) : PageModel
{
    private const int DefaultPageSize = 25;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Language { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MediaType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "CreatedAt";

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = "desc";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DefaultPageSize;

    public IReadOnlyList<TorrentListItemViewModel> Torrents { get; private set; } = [];

    public int TotalCount { get; private set; }

    public async Task OnGetAsync()
    {
        Torrents = await LoadAsync();
    }

    public async Task<IActionResult> OnPostBulkAsync(string action, List<string> selectedIds)
    {
        switch (action)
        {
            case "clean":
                var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
                foreach (var id in selectedIds)
                {
                    await cleaner.CleanOneAsync(id, connections);
                }

                break;
            case "refresh":
                foreach (var id in selectedIds)
                {
                    await linkRunner.RefreshAsync(id, relink: false);
                }

                break;
            case "removeRecord":
                var toRemove = await dbContext.Torrents.Where(t => selectedIds.Contains(t.Id)).ToListAsync();
                dbContext.Torrents.RemoveRange(toRemove);
                await dbContext.SaveChangesAsync();
                break;
        }

        return RedirectToPage();
    }

    private async Task<List<TorrentListItemViewModel>> LoadAsync()
    {
        var query = dbContext.Torrents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            // Fuzzy match over title/authors/series (§10.2): Title is indexed; Authors/Series live
            // inside the serialized metadata blob, so a plain substring search covers them too.
            query = query.Where(t => t.Title.Contains(Q) || t.MetadataJson.Contains(Q));
        }

        if (!string.IsNullOrWhiteSpace(Language))
        {
            query = query.Where(t => t.MetadataJson.Contains($"\"Language\":\"{Language}\""));
        }

        if (!string.IsNullOrWhiteSpace(MediaType))
        {
            query = query.Where(t => t.MetadataJson.Contains($"\"MediaType\":\"{MediaType}\""));
        }

        // Sorting/paging happens in memory, not via ORDER BY/OFFSET in SQL: the SQLite EF Core
        // provider cannot translate an ORDER BY over a DateTimeOffset column (CreatedAt), and this
        // app's realistic data volume (a self-hosted single-user library) makes that an acceptable
        // trade-off against adding a global DateTimeOffset value-conversion/migration for it.
        var matched = await query.ToListAsync();
        IEnumerable<TorrentEntity> ordered = (Sort, Dir) switch
        {
            ("Title", "asc") => matched.OrderBy(t => t.Title, StringComparer.Ordinal),
            ("Title", _) => matched.OrderByDescending(t => t.Title, StringComparer.Ordinal),
            (_, "asc") => matched.OrderBy(t => t.CreatedAt),
            _ => matched.OrderByDescending(t => t.CreatedAt),
        };

        TotalCount = matched.Count;

        var pageSize = PageSize <= 0 ? DefaultPageSize : PageSize;
        var entities = ordered.Skip((Math.Max(PageNumber, 1) - 1) * pageSize).Take(pageSize).ToList();
        return entities.Select(TorrentListItemViewModel.From).ToList();
    }
}
