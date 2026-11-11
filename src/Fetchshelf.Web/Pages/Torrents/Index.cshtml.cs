using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
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
    FetchshelfDbContext dbContext, LibraryQueries libraryQueries, FormatPreferenceLists formatPreferenceLists, LinkRunner linkRunner, Cleaner cleaner,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory, ClientStatusChecker clientStatusChecker,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Library { get; set; }

    /// <summary>The torrent's qBittorrent category. Not called Category: the bulk form posts a `category` of its own.</summary>
    [BindProperty(SupportsGet = true)]
    public string? QbCategory { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Problem { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Language { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MediaType { get; set; }

    /// <summary>An author's name key (<see cref="PersonNameKey"/>), set by following an author's name.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Author { get; set; }

    /// <summary>A narrator's name key, set by following a narrator's name.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Narrator { get; set; }

    /// <summary>A series' name key (<see cref="SeriesNameKey"/>), set by following a series name.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Series { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "CreatedAt";

    [BindProperty(SupportsGet = true)]
    public string Dir { get; set; } = "desc";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    public TorrentListQuery Query => new(Q, Status, Library, QbCategory, Problem, Language, MediaType, Sort, Dir, PageSize, Author, Narrator, Series);

    /// <summary>The author, narrator and series filters in use, with the names they stand for.</summary>
    public IReadOnlyList<NameFilter> NameFilters { get; private set; } = [];

    /// <summary>What is followed, for the follow buttons by the author and series filters; empty without a `[follow]` section.</summary>
    public IReadOnlyDictionary<(FollowKind, string), FollowMode> Followed { get; private set; } = new Dictionary<(FollowKind, string), FollowMode>();

    /// <summary>The choices for the Library, Language and Media type filters: the values among all tracked torrents.</summary>
    public IReadOnlyList<string> LibraryOptions { get; private set; } = [];

    public IReadOnlyList<string> QbCategoryOptions { get; private set; } = [];

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

    /// <summary>The library categories the bulk category dropdown offers.</summary>
    public IReadOnlyList<LibraryCategory> CategoryOptions => LibraryCategories.For(settings);

    public async Task<IActionResult> OnPostBulkAsync(string action, List<string> selectedIds, string? category)
    {
        switch (action)
        {
            case "setCategory" when !LibraryCategories.IsLibraryCategory(settings, category):
                TempData["CheckMessage"] = "Choose one of your libraries' categories first.";
                TempData["CheckFailed"] = true;
                break;
            case "setCategory":
                await RunEachAsync(selectedIds, $"Set the category to {category} for", "Setting the category", async (id, connections) =>
                {
                    var client = await QBittorrentConnectionResolver.ResolveAsync(
                        connections.Select(c => c.Client).ToList(), id, HttpContext.RequestAborted);
                    if (client is null)
                    {
                        return (false, "qBittorrent doesn't have it.");
                    }

                    await client.SetCategoryAsync(id, category!, HttpContext.RequestAborted);
                    // Shown in the list straight away; the Link job keeps it in step with qBittorrent from now on.
                    var torrent = await dbContext.Torrents.FirstAsync(t => t.Id == id, HttpContext.RequestAborted);
                    torrent.QBittorrentCategory = category;
                    await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
                    return (true, string.Empty);
                });
                break;
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
        // The filters on columns and on the author and series rows run in SQL. Status, problems, the search text and
        // the sort still run in memory on what is left: the SQLite EF Core provider cannot translate an ORDER BY over a
        // DateTimeOffset column (CreatedAt), SQLite's substring match is case-sensitive, and the search text is matched
        // against the serialized metadata. This app's realistic data volume (a self-hosted single-user library) makes
        // that an acceptable trade-off.
        var torrents = dbContext.Torrents.AsNoTracking();

        // The value in use stays a choice even when no torrent has it any more, so the dropdown still shows it.
        LibraryOptions = Distinct((await torrents.Select(t => t.LinkerName).Distinct().ToListAsync()).Append(Library));
        QbCategoryOptions = Distinct((await torrents.Select(t => t.QBittorrentCategory).Distinct().ToListAsync()).Append(QbCategory));
        LanguageOptions = Distinct((await torrents.Select(t => t.Language).Distinct().ToListAsync()).Append(Language));
        MediaTypeOptions = Distinct((await torrents.Select(t => t.MediaType).Distinct().ToListAsync()).Append(MediaType));
        NameFilters = await LoadNameFiltersAsync();
        if (settings.Follow is not null && NameFilters.Any(f => f.Parameter != nameof(Narrator)))
        {
            Followed = await Following.FollowButtonsViewModel.LoadFollowedAsync(dbContext);
        }

        if (!string.IsNullOrEmpty(Library))
        {
            torrents = torrents.Where(t => t.LinkerName == Library);
        }

        if (!string.IsNullOrEmpty(QbCategory))
        {
            torrents = torrents.Where(t => t.QBittorrentCategory == QbCategory);
        }

        if (!string.IsNullOrEmpty(Language))
        {
            torrents = torrents.Where(t => t.Language == Language);
        }

        if (!string.IsNullOrEmpty(MediaType))
        {
            torrents = torrents.Where(t => t.MediaType == MediaType);
        }

        if (!string.IsNullOrEmpty(Author))
        {
            var ids = libraryQueries.TorrentIdsWithPerson(PersonRole.Author, Author);
            torrents = torrents.Where(t => ids.Contains(t.Id));
        }

        if (!string.IsNullOrEmpty(Narrator))
        {
            var ids = libraryQueries.TorrentIdsWithPerson(PersonRole.Narrator, Narrator);
            torrents = torrents.Where(t => ids.Contains(t.Id));
        }

        if (!string.IsNullOrEmpty(Series))
        {
            var ids = libraryQueries.TorrentIdsInSeries(Series);
            torrents = torrents.Where(t => ids.Contains(t.Id));
        }

        // The metadata read for the search is kept for the rows shown, so no row's JSON is read twice.
        var metadataById = new Dictionary<string, TorrentMetadata>();
        var matched = (await torrents.ToListAsync())
            .Where(t => TorrentListFilters.MatchesStatus(t, Status))
            .Where(t => TorrentListFilters.MatchesProblem(t, Problem, formatPreferenceLists))
            .Where(t => string.IsNullOrWhiteSpace(Q) || TorrentSearchMatcher.Matches(metadataById[t.Id] = TorrentMetadataSerializer.Deserialize(t.MetadataJson), Q))
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
        return entities
            .Select(t => TorrentListItemViewModel.From(t, metadataById.GetValueOrDefault(t.Id)) with { UpgradeNote = UpgradeNote.For(t, formatPreferenceLists) })
            .ToList();
    }

    /// <summary>
    /// The author, narrator and series filters, each with the name it stands for. A key no torrent has any more still
    /// shows, as the key itself, so the filter can be dropped.
    /// </summary>
    private async Task<List<NameFilter>> LoadNameFiltersAsync()
    {
        var filters = new List<NameFilter>();
        if (!string.IsNullOrEmpty(Author))
        {
            filters.Add(new NameFilter(nameof(Author), Author, "Author",
                await libraryQueries.PersonNameAsync(PersonRole.Author, Author) ?? Author, Query with { Author = null }));
        }

        if (!string.IsNullOrEmpty(Narrator))
        {
            filters.Add(new NameFilter(nameof(Narrator), Narrator, "Narrator",
                await libraryQueries.PersonNameAsync(PersonRole.Narrator, Narrator) ?? Narrator, Query with { Narrator = null }));
        }

        if (!string.IsNullOrEmpty(Series))
        {
            filters.Add(new NameFilter(nameof(Series), Series, "Series",
                await libraryQueries.SeriesNameAsync(Series) ?? Series, Query with { Series = null }));
        }

        return filters;
    }

    private static List<string> Distinct(IEnumerable<string?> values) =>
        values.OfType<string>().Where(v => v.Length > 0).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>An author, narrator or series filter in use: its query parameter and key, and the name shown for it.</summary>
public sealed record NameFilter(string Parameter, string Key, string Label, string Name, TorrentListQuery Without);
