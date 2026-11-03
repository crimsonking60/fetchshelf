using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Torrents;

public class IndexModel(
    FetchshelfDbContext dbContext, LinkRunner linkRunner, Cleaner cleaner,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory, ClientStatusChecker clientStatusChecker,
    ILogger<IndexModel> logger) : PageModel
{
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
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public TorrentListQuery Query => new(Q, Language, MediaType, Sort, Dir, PageSize);

    public IReadOnlyList<TorrentListItemViewModel> Torrents { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Torrents/Index", Query.ToRouteValues(), PageNumber, TotalPages, Torrents.Count, TotalCount, "torrent-pager", "Torrent pages");

    /// <summary>
    /// The outcome of a "Check qBittorrent" or a bulk "Relink", shown above the list; set by the htmx
    /// response itself or, after a redirect, read back from TempData.
    /// </summary>
    public string? CheckMessage { get; private set; }

    public bool CheckFailed { get; private set; }

    public async Task OnGetAsync()
    {
        CheckMessage = TempData["CheckMessage"] as string;
        CheckFailed = TempData["CheckFailed"] as bool? ?? false;
        Torrents = await LoadAsync();
    }

    /// <summary>
    /// Asks qBittorrent for the current state of every tracked torrent and refreshes the stored status. From the page's htmx
    /// form it answers with the re-rendered list (same filters, sort and page); without htmx it redirects back to it.
    /// </summary>
    public async Task<IActionResult> OnPostCheckClientAsync()
    {
        var clients = QBittorrentConnections.Build(settings, qbittorrentFactory).Select(c => c.Client).ToList();
        var result = await clientStatusChecker.CheckAsync(clients, HttpContext.RequestAborted);
        CheckFailed = !result.Succeeded;
        CheckMessage = Describe(result);

        if (!Request.Headers.ContainsKey("HX-Request"))
        {
            // Plain TempData entries rather than [TempData] properties, so the htmx path below leaves nothing behind for the next page load.
            TempData["CheckMessage"] = CheckMessage;
            TempData["CheckFailed"] = CheckFailed;
            return RedirectToPage(new { Q, Language, MediaType, Sort, Dir, PageNumber, PageSize });
        }

        Torrents = await LoadAsync();
        return Partial("_TorrentListPartial", this);
    }

    private static string Describe(ClientStatusCheckResult result)
    {
        var torrents = result.Checked == 1 ? "1 torrent" : $"{result.Checked} torrents";
        var retired = result.RetiredSelections switch
        {
            0 => string.Empty,
            1 => " 1 finished download was removed from the Selected queue.",
            var count => $" {count} finished downloads were removed from the Selected queue.",
        };
        var skipped = result.MetadataOnlySkipped switch
        {
            0 => string.Empty,
            1 => " 1 metadata-only record skipped.",
            var count => $" {count} metadata-only records skipped.",
        };
        return result.Succeeded
            ? $"Checked {torrents} in qBittorrent: {result.FoundInClient} found, {result.NotInClient} not in client.{retired}{skipped}"
            : $"qBittorrent could not be fully checked - {string.Join("; ", result.Errors)}. "
                + $"{result.FoundInClient} of {torrents} updated; the rest were left unchanged.{retired}{skipped}";
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
                    await linkRunner.RefreshAsync(id);
                }

                break;
            case "relink":
                var (message, failed) = await RelinkAsync(selectedIds);
                TempData["CheckMessage"] = message;
                TempData["CheckFailed"] = failed;
                break;
            case "removeRecord":
                var toRemove = await dbContext.Torrents.Where(t => selectedIds.Contains(t.Id)).ToListAsync();
                dbContext.Torrents.RemoveRange(toRemove);
                await dbContext.SaveChangesAsync();
                break;
        }

        return RedirectToPage(new { Q, Language, MediaType, Sort, Dir, PageNumber, PageSize });
    }

    /// <summary>Relinks each torrent in turn; one that is refused or fails is reported and the rest still run.</summary>
    private async Task<(string Message, bool Failed)> RelinkAsync(List<string> ids)
    {
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        var titles = await dbContext.Torrents.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title, HttpContext.RequestAborted);
        var relinked = 0;
        var problems = new List<string>();
        foreach (var id in ids.Where(titles.ContainsKey))
        {
            try
            {
                var result = await linkRunner.RelinkAsync(id, connections, HttpContext.RequestAborted);
                if (result.Succeeded)
                {
                    relinked++;
                }
                else
                {
                    problems.Add($"{titles[id]}: {result.Message}");
                }
            }
            catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Relink of {TorrentId} failed", id);
                // What the failed relink changed must not be saved along with the next torrent.
                dbContext.DiscardPendingChanges();
                problems.Add($"{titles[id]}: Relinking failed: {MamErrorDescriber.DescribeAmong(ex, "qBittorrent or Audiobookshelf")}");
            }
        }

        var total = relinked + problems.Count;
        var torrents = total == 1 ? "1 torrent" : $"{total} torrents";
        var summary = problems.Count == 0
            ? $"Relinked {torrents}."
            : $"Relinked {relinked} of {torrents}. {string.Join(" ", problems)}";
        return (summary, problems.Count > 0);
    }

    private async Task<List<TorrentListItemViewModel>> LoadAsync()
    {
        var query = dbContext.Torrents.AsNoTracking().AsQueryable();

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

        // The search runs here rather than in SQL: SQLite's substring match is case-sensitive, and authors and
        // series are inside the serialized metadata, where a raw substring match would also hit descriptions.
        if (!string.IsNullOrWhiteSpace(Q))
        {
            matched = matched.Where(t => TorrentSearchMatcher.Matches(TorrentMetadataSerializer.Deserialize(t.MetadataJson), Q)).ToList();
        }

        IEnumerable<TorrentEntity> ordered = (Sort, Dir) switch
        {
            ("Title", "asc") => matched.OrderBy(t => t.Title, StringComparer.Ordinal),
            ("Title", _) => matched.OrderByDescending(t => t.Title, StringComparer.Ordinal),
            (_, "asc") => matched.OrderBy(t => t.CreatedAt),
            _ => matched.OrderByDescending(t => t.CreatedAt),
        };

        TotalCount = matched.Count;

        (var entities, PageNumber, TotalPages) = Paging.Slice(ordered, TotalCount, PageNumber, PageSize);
        return entities.Select(TorrentListItemViewModel.From).ToList();
    }
}
