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
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Library { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Problem { get; set; }

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

    public TorrentListQuery Query => new(Q, Status, Library, Problem, Language, MediaType, Sort, Dir, PageSize);

    /// <summary>The choices for the Library, Language and Media type filters: the values among all tracked torrents.</summary>
    public IReadOnlyList<string> LibraryOptions { get; private set; } = [];

    public IReadOnlyList<string> LanguageOptions { get; private set; } = [];

    public IReadOnlyList<string> MediaTypeOptions { get; private set; } = [];

    public IReadOnlyList<TorrentListItemViewModel> Torrents { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public PagerViewModel Pager =>
        new("/Torrents/Index", Query.ToRouteValues(), PageNumber, TotalPages, Torrents.Count, TotalCount, PageSize, "torrent-pager", "Torrent pages");

    /// <summary>
    /// The outcome of a "Check qBittorrent" or a bulk relink or unlink, shown above the list; set by the htmx
    /// response itself or, after a redirect, read back from TempData.
    /// </summary>
    public string? CheckMessage { get; private set; }

    public bool CheckFailed { get; private set; }

    public async Task OnGetAsync()
    {
        CheckMessage = TempData["CheckMessage"] as string;
        CheckFailed = TempData["CheckFailed"] as bool? ?? false;
        PageSize = PageSizeCookie.Apply(HttpContext, "torrents", PageSize);
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
            return RedirectToPage(CurrentPageRouteValues());
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
                await RunEachAsync(selectedIds, "Relinked", "Relinking", async (id, connections) =>
                {
                    var result = await linkRunner.RelinkAsync(id, connections, HttpContext.RequestAborted);
                    return (result.Succeeded, result.Message);
                });
                break;
            case "unlink":
                await RunEachAsync(selectedIds, "Unlinked", "Unlinking", async (id, connections) =>
                {
                    var result = await cleaner.UnlinkAsync(id, connections, HttpContext.RequestAborted);
                    return (result.Succeeded, result.Message);
                });
                break;
            case "removeRecord":
                var toRemove = await dbContext.Torrents.Where(t => selectedIds.Contains(t.Id)).ToListAsync();
                dbContext.Torrents.RemoveRange(toRemove);
                await dbContext.SaveChangesAsync();
                break;
        }

        return RedirectToPage(CurrentPageRouteValues());
    }

    /// <summary>
    /// Runs a relink or unlink on each torrent in turn and leaves a summary for the page; one that is refused or fails
    /// is reported and the rest still run.
    /// </summary>
    private async Task RunEachAsync(
        List<string> ids, string doneVerb, string failedVerb,
        Func<string, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)>, Task<(bool Succeeded, string Message)>> action)
    {
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        var titles = await dbContext.Torrents.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title, HttpContext.RequestAborted);
        var done = 0;
        var problems = new List<string>();
        foreach (var id in ids.Where(titles.ContainsKey))
        {
            try
            {
                var (succeeded, message) = await action(id, connections);
                if (succeeded)
                {
                    done++;
                }
                else
                {
                    problems.Add($"{titles[id]}: {message}");
                }
            }
            catch (Exception ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Action} {TorrentId} failed", failedVerb, id);
                // What the failed attempt changed must not be saved along with the next torrent.
                dbContext.DiscardPendingChanges();
                problems.Add($"{titles[id]}: {failedVerb} failed: {MamErrorDescriber.DescribeAmong(ex, "qBittorrent or Audiobookshelf")}");
            }
        }

        var total = done + problems.Count;
        var torrents = total == 1 ? "1 torrent" : $"{total} torrents";
        TempData["CheckMessage"] = problems.Count == 0
            ? $"{doneVerb} {torrents}."
            : $"{doneVerb} {done} of {torrents}. {string.Join(" ", problems)}";
        TempData["CheckFailed"] = problems.Count > 0;
    }

    /// <summary>The search, filters, sort and page, so a redirect comes back to the same list.</summary>
    private RouteValueDictionary CurrentPageRouteValues()
    {
        var values = new RouteValueDictionary();
        foreach (var (name, value) in Query.ToRouteValues())
        {
            values[name] = value;
        }

        values[nameof(PageNumber)] = PageNumber;
        return values;
    }

    private async Task<List<TorrentListItemViewModel>> LoadAsync()
    {
        // Filtering, sorting and paging happen in memory, not in SQL: the SQLite EF Core provider cannot translate an
        // ORDER BY over a DateTimeOffset column (CreatedAt), SQLite's substring match is case-sensitive, and the
        // language, media type, authors and series are inside the serialized metadata. This app's realistic data
        // volume (a self-hosted single-user library) makes that an acceptable trade-off.
        var all = (await dbContext.Torrents.AsNoTracking().ToListAsync())
            .Select(t => (Entity: t, Metadata: TorrentMetadataSerializer.Deserialize(t.MetadataJson)))
            .ToList();

        // The value in use stays a choice even when no torrent has it any more, so the dropdown still shows it.
        LibraryOptions = Distinct(all.Select(t => t.Entity.LinkerName).Append(Library));
        LanguageOptions = Distinct(all.Select(t => t.Metadata.Language.ToString()).Append(Language));
        MediaTypeOptions = Distinct(all.Select(t => t.Metadata.MediaType.ToString()).Append(MediaType));

        var matched = all
            .Where(t => TorrentListFilters.MatchesStatus(t.Entity, Status))
            .Where(t => string.IsNullOrEmpty(Library) || t.Entity.LinkerName == Library)
            .Where(t => TorrentListFilters.MatchesProblem(t.Entity, Problem))
            .Where(t => string.IsNullOrEmpty(Language) || t.Metadata.Language.ToString() == Language)
            .Where(t => string.IsNullOrEmpty(MediaType) || t.Metadata.MediaType.ToString() == MediaType)
            .Where(t => string.IsNullOrWhiteSpace(Q) || TorrentSearchMatcher.Matches(t.Metadata, Q))
            .Select(t => t.Entity)
            .ToList();

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

    private static List<string> Distinct(IEnumerable<string?> values) =>
        values.OfType<string>().Where(v => v.Length > 0).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
}
