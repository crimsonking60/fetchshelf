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
    FetchshelfDbContext dbContext, LibraryQueries libraryQueries, FormatPreferenceLists formatPreferenceLists,
    FetchshelfSettings settings, QBittorrentClientFactory qbittorrentFactory, ClientStatusChecker clientStatusChecker,
    BulkOperationQueue bulkOperations) : PageModel
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

    /// <summary>The Seeding filter (<see cref="TorrentListFilters.SeedingOptions"/>); offered only with a `[seeding]` section.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Seeding { get; set; }

    /// <summary>MAM's seeding requirement from `[seeding]`; null without the section, when nothing is estimated.</summary>
    public SeedingRequirement? SeedingRequirement => settings.Seeding?.Requirement;

    /// <summary>"needs 31 h", "not seeding, needs 31 h" or "past the period" for a torrent that isn't satisfied; null otherwise.</summary>
    public string? SeedingNote(TorrentListItemViewModel torrent)
    {
        if (SeedingRequirement is not { } requirement || !Enum.TryParse<SeedingState>(torrent.SeedingState, out var state))
        {
            return null;
        }

        var needed = (long)Math.Ceiling(requirement.SeedHours - TimeSpan.FromSeconds(torrent.SeedingSeconds ?? 0).TotalHours);
        return state switch
        {
            SeedingState.SeedingUnsatisfied => $"needs {needed} h",
            SeedingState.NotSeedingUnsatisfied => $"not seeding, needs {needed} h",
            SeedingState.PastThePeriod => "past the period",
            _ => null,
        };
    }

    /// <summary>
    /// <see cref="ShowCollectionBooks"/> to list the books of split collections too. Without it each collection is one row,
    /// and a book's row shows only when the search or a title, author, narrator or series filter looks for it.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? CollectionBooks { get; set; }

    public const string ShowCollectionBooks = "show";

    /// <summary>Whether any collection is linked as its books, so the page offers <see cref="CollectionBooks"/>.</summary>
    public bool HasCollectionBooks { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Language { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MediaType { get; set; }

    /// <summary>A genre by MAM's id, or <see cref="GenreFilters.NoGenre"/>. Used only with `genres = true`.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Genre { get; set; }

    /// <summary><c>Fiction</c>, <c>Nonfiction</c> or <see cref="GenreFilters.Neither"/>. Used only with `genres = true`.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Fiction { get; set; }

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

    public TorrentListQuery Query => new(
        Q, Status, Library, QbCategory, Problem, Language, MediaType, Sort, Dir, PageSize, Author, Narrator, Series, Seeding, CollectionBooks,
        GenresOn ? Genre : null, GenresOn ? Fiction : null);

    /// <summary>Whether `genres = true`, which shows the Genre and Fiction filters.</summary>
    public bool GenresOn => settings.Genres;

    /// <summary>The Genre filter's choices: the genres among the tracked torrents, by MAM's names, and No genre.</summary>
    public IReadOnlyList<(string Value, string Label)> GenreOptions { get; private set; } = [];

    /// <summary>The Fiction filter's choices: Neither only when a tracked torrent has neither main category.</summary>
    public IReadOnlyList<(string Value, string Label)> FictionOptions { get; private set; } = GenreFilters.FictionOptions;

    /// <summary>The dropdown filters in use, each with the query that drops it, shown as chips under the filters.</summary>
    public IReadOnlyList<FilterChip> FilterChips
    {
        get
        {
            var query = Query;
            List<FilterChip> chips = [];
            void Add(string label, string? value, Func<TorrentListQuery, TorrentListQuery> without)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    chips.Add(new FilterChip(label, value, without(query)));
                }
            }

            static string LabelOf(IReadOnlyList<(string Value, string Label)> options, string? value) =>
                options.FirstOrDefault(o => o.Value == value).Label ?? value ?? string.Empty;

            Add("Status", LabelOf(TorrentListFilters.StatusOptions, Status), q => q with { Status = null });
            Add("Library", Library, q => q with { Library = null });
            Add("Category", QbCategory, q => q with { QbCategory = null });
            Add("Problems", LabelOf(TorrentListFilters.ProblemOptions, Problem), q => q with { Problem = null });
            Add("Seeding", LabelOf(TorrentListFilters.SeedingOptions, Seeding), q => q with { Seeding = null });
            Add("Collections", CollectionBooks == ShowCollectionBooks ? "books too" : CollectionBooks, q => q with { CollectionBooks = null });
            Add("Media type", MediaType, q => q with { MediaType = null });
            Add("Language", Language, q => q with { Language = null });
            if (GenresOn)
            {
                Add("Fiction", GenreFilters.FictionLabelOf(Fiction), q => q with { Fiction = null });
                Add("Genre", GenreFilters.LabelOf(Genre), q => q with { Genre = null });
            }

            return chips;
        }
    }

    /// <summary>The author, narrator and series filters in use, with the names they stand for.</summary>
    public IReadOnlyList<NameFilter> NameFilters { get; private set; } = [];

    /// <summary>What is followed, for the follow buttons by the author and series filters; empty without a `[follow]` section.</summary>
    public IReadOnlyDictionary<(FollowKind, string), Following.FollowState> Followed { get; private set; } = new Dictionary<(FollowKind, string), Following.FollowState>();

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
    /// The outcome of a "Check qBittorrent", or why a bulk action wasn't queued, shown above the list; set by the htmx
    /// response itself or, after a redirect, read back from TempData.
    /// </summary>
    public string? CheckMessage { get; private set; }

    public bool CheckFailed { get; private set; }

    /// <summary>The cookie that remembers the Filters panel left open ("open") or closed ("closed"); remember-open.js sets it.</summary>
    public const string FiltersOpenCookie = "fs-torrent-filters";

    /// <summary>The Filters panel starts open while a filter is in use, or when it was left open on an earlier visit.</summary>
    public bool FiltersOpen => Query.HasFilters || Request.Cookies[FiltersOpenCookie] == "open";

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

    /// <summary>The categories the bulk category dropdown offers: the libraries', then `extra_categories`.</summary>
    public IReadOnlyList<LibraryCategory> CategoryOptions => LibraryCategories.For(settings);

    /// <summary>
    /// A bulk action on the torrents ticked. Remove record is one database delete and is done here; the others are queued
    /// for <see cref="BulkOperationWorker"/>, whose progress the page shows above the list, and the page comes back at once.
    /// From htmx (the page's script), only what changed comes back, so the list keeps its ticks and where it was scrolled
    /// to: the progress panel for a queued action, the list for Remove record. Without a script the page reloads.
    /// </summary>
    public async Task<IActionResult> OnPostBulkAsync(string action, List<string> selectedIds, string? category)
    {
        BulkAction? queued = action switch
        {
            "relink" => BulkAction.Relink,
            "unlink" => BulkAction.Unlink,
            "refresh" => BulkAction.Refresh,
            "clean" => BulkAction.Clean,
            "setCategory" => BulkAction.SetCategory,
            _ => null,
        };
        var fromHtmx = Request.Headers.ContainsKey("HX-Request");
        if (queued == BulkAction.SetCategory && !LibraryCategories.IsOffered(settings, category))
        {
            TempData["CheckMessage"] = "Choose a category first.";
            TempData["CheckFailed"] = true;
            if (fromHtmx)
            {
                // The message is shown at the top of the page, so the page reloads to show it.
                Response.Headers["HX-Refresh"] = "true";
                return Content(string.Empty);
            }
        }
        else if (queued is { } bulkAction && selectedIds.Count > 0)
        {
            bulkOperations.Enqueue(bulkAction, selectedIds.Distinct(StringComparer.Ordinal).ToList(), bulkAction == BulkAction.SetCategory ? category : null);
        }
        else if (action == "removeRecord")
        {
            // A collection's books go with it: they are derived from it.
            var toRemove = await dbContext.Torrents
                .Where(t => selectedIds.Contains(t.Id) || (t.CollectionTorrentId != null && selectedIds.Contains(t.CollectionTorrentId)))
                .ToListAsync();
            dbContext.Torrents.RemoveRange(toRemove);
            await dbContext.SaveChangesAsync();
            if (fromHtmx)
            {
                // The rows are gone, so the list comes back in place of the old one, without them.
                Torrents = await LoadAsync();
                Response.Headers["HX-Retarget"] = "#torrent-list-body";
                Response.Headers["HX-Reswap"] = "innerHTML";
                return Partial("_TorrentListPartial", this);
            }
        }

        return fromHtmx ? OnGetBulkOperations() : RedirectToPage(CurrentPageRouteValues());
    }

    /// <summary>The bulk actions running, waiting or ended, for the panel above the list.</summary>
    public BulkOperationsViewModel BulkOperations => new(bulkOperations.Shown(BulkOperationPage.Torrents), "/Torrents/Index");

    /// <summary>The panel above the list, which polls itself while an operation runs or waits.</summary>
    public PartialViewResult OnGetBulkOperations() => Partial("_BulkOperationsPartial", BulkOperations);

    /// <summary><b>Cancel</b> on an operation: one waiting never starts, one running stops after its current torrent.</summary>
    public IActionResult OnPostCancelBulk(int operationId)
    {
        bulkOperations.Cancel(operationId);
        return BulkPanelOrPage();
    }

    /// <summary><b>Dismiss</b> on an ended operation: its summary is no longer shown.</summary>
    public IActionResult OnPostDismissBulk(int operationId)
    {
        bulkOperations.Dismiss(operationId);
        return BulkPanelOrPage();
    }

    private IActionResult BulkPanelOrPage() =>
        Request.Headers.ContainsKey("HX-Request") ? OnGetBulkOperations() : RedirectToPage(CurrentPageRouteValues());

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

    /// <summary>The address of the export in <paramref name="format"/>, with the search, filters and sort in use.</summary>
    public Dictionary<string, string> ExportRouteValues(string format)
    {
        // In the same dictionary: asp-all-route-data replaces a separate asp-route-format.
        var values = Query.ToRouteValues();
        values["format"] = format;
        return values;
    }

    /// <summary>
    /// Downloads the list as it is filtered, searched and sorted now, every page of it, as `csv` or `json`. The rows are
    /// written to the response one at a time.
    /// </summary>
    public async Task<IActionResult> OnGetExportAsync(string? format)
    {
        var json = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
        var (ordered, metadataById) = await FilterAsync();
        var linkedAt = (await dbContext.Events.AsNoTracking()
                .Where(e => e.Kind == EventKind.Linked && e.TorrentId != null)
                .Select(e => new { e.TorrentId, e.OccurredAt })
                .ToListAsync(HttpContext.RequestAborted))
            .GroupBy(e => e.TorrentId!)
            .ToDictionary(group => group.Key, group => group.Max(e => e.OccurredAt));
        var rows = ordered.Select(t => LibraryExportRow.From(
            t,
            metadataById.GetValueOrDefault(t.Id) ?? TorrentMetadataSerializer.Deserialize(t.MetadataJson),
            TorrentListFilters.StatusValue(TorrentListFilters.StatusOf(t)),
            linkedAt.TryGetValue(t.Id, out var linked) ? linked : null));

        var fileName = $"fetchshelf-torrents-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{(json ? "json" : "csv")}";
        Response.ContentType = json ? "application/json; charset=utf-8" : "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        if (json)
        {
            await LibraryExport.WriteJsonAsync(rows, Response.Body, HttpContext.RequestAborted);
        }
        else
        {
            // With a byte order mark, so a spreadsheet opens the UTF-8 titles and names as UTF-8.
            await using var writer = new StreamWriter(Response.Body, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await LibraryExport.WriteCsvAsync(rows, writer, HttpContext.RequestAborted);
        }

        return new EmptyResult();
    }

    private async Task<List<TorrentListItemViewModel>> LoadAsync()
    {
        var (ordered, metadataById) = await FilterAsync();
        (var entities, PageNumber, TotalPages) = Paging.Slice(ordered, TotalCount, PageNumber, PageSize);
        return entities
            .Select(t => TorrentListItemViewModel.From(t, metadataById.GetValueOrDefault(t.Id)) with { UpgradeNote = UpgradeNote.For(t, formatPreferenceLists) })
            .ToList();
    }

    /// <summary>
    /// The torrents the search and filters let through, in the chosen order, with the metadata read for the search
    /// (by torrent id) so no row's JSON is read twice. Also fills in the filter choices and <see cref="TotalCount"/>.
    /// </summary>
    private async Task<(IEnumerable<TorrentEntity> Ordered, Dictionary<string, TorrentMetadata> MetadataById)> FilterAsync()
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

        HasCollectionBooks = await torrents.AnyAsync(t => t.CollectionTorrentId != null);
        var lookingForABook = !string.IsNullOrWhiteSpace(Q) || !string.IsNullOrEmpty(Author) || !string.IsNullOrEmpty(Narrator) || !string.IsNullOrEmpty(Series);
        if (CollectionBooks != ShowCollectionBooks && !lookingForABook)
        {
            torrents = torrents.Where(t => t.CollectionTorrentId == null);
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

        if (GenresOn)
        {
            GenreOptions = GenreFilters.Options(await dbContext.TorrentGenres.Select(g => g.Genre).Distinct().ToListAsync(), Genre);
            FictionOptions = GenreFilters.FictionChoices(await dbContext.Torrents.AnyAsync(t => t.FictionCategory == null), Fiction);
            torrents = GenreFilters.Apply(torrents, dbContext.TorrentGenres, Genre, Fiction);
        }

        if (Seeding == TorrentListFilters.LooksUnsatisfied)
        {
            torrents = torrents.Where(t => t.SeedingState == nameof(SeedingState.SeedingUnsatisfied) || t.SeedingState == nameof(SeedingState.NotSeedingUnsatisfied));
        }
        else if (!string.IsNullOrEmpty(Seeding))
        {
            torrents = torrents.Where(t => t.SeedingState == Seeding);
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

        var descending = SortState.From(Sort, Dir).Descending;
        IEnumerable<TorrentEntity> ordered = Sort switch
        {
            "Title" => Order(matched, t => t.Title, descending),
            "Format" => Order(matched, t => $"{t.MediaType}\u0001{t.Language}", descending),
            "Category" => Order(matched, t => t.QBittorrentCategory ?? string.Empty, descending),
            "Status" => Order(matched, t => t.ClientStatus ?? string.Empty, descending),
            _ => Order(matched, t => t.CreatedAt, descending),
        };

        TotalCount = matched.Count;
        return (ordered, metadataById);
    }

    /// <summary>Orders by one column in the direction asked for; equal values keep a stable order by title, then id.</summary>
    private static IEnumerable<TorrentEntity> Order<TKey>(IEnumerable<TorrentEntity> source, Func<TorrentEntity, TKey> key, bool descending)
    {
        var comparer = typeof(TKey) == typeof(string) ? (IComparer<TKey>)(object)StringComparer.Ordinal : Comparer<TKey>.Default;
        var ordered = descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
        return ordered.ThenBy(t => t.Title, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal);
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

/// <summary>A dropdown filter in use on the Torrents page: its name and value, and the query without it.</summary>
public sealed record FilterChip(string Label, string Value, TorrentListQuery Without);
