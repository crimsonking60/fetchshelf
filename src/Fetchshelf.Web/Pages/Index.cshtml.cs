using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Jobs;
using Fetchshelf.Web.Pages.Torrents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The Search page: manual MAM search with the shared filter, cost badges, and
/// download / download-with-wedge actions that start a selection via tag rules. Also serves as the
/// application's home page ("/").
/// </summary>
public class IndexModel(
    MamClient mamClient, MamClientOptions mamOptions, SelectionRunner selectionRunner, FetchshelfSettings settings,
    FormatPreferenceLists formatPreferenceLists, FetchshelfDbContext dbContext, IJobRegistry jobRegistry,
    QBittorrentPresenceCache qbittorrentPresence, QBittorrentClientFactory qbittorrentFactory,
    ILogger<IndexModel> logger) : PageModel
{
    /// <summary>What happened to the last Download click, shown once after the redirect back to this page.</summary>
    [TempData]
    public string? DownloadMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>
    /// The language to search in: unset for the top-level <c>languages</c>, <see cref="AnyLanguage"/> for every
    /// language, or one <see cref="Core.Metadata.Language"/> name.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Lang { get; set; }

    public const string AnyLanguage = "any";

    /// <summary>The fields the search text is looked for in (<see cref="SearchQueryBuilder.FieldOptions"/>); title and author when none.</summary>
    [BindProperty(SupportsGet = true)]
    public List<string> In { get; set; } = [];

    /// <summary>One of <see cref="SearchQueryBuilder.SortOptions"/>; MAM's own order when unset.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    /// <summary>One of <see cref="SearchQueryBuilder.TypeOptions"/>; every torrent when unset.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    /// <summary>The page of results, from 1. Not called Page: Razor Pages keeps that name for itself.</summary>
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    /// <summary>The search's choices, each made known and in range once; the links, the heading and the query read these.</summary>
    public SearchChoices Choices => _choices ??= SearchQueryBuilder.Choices(Q, In, Sort, Type, PageNumber);

    private SearchChoices? _choices;

    public IReadOnlyList<string> SearchFields => Choices.Fields;

    public string SearchSort => Choices.Sort;

    public string SearchType => Choices.Type;

    /// <summary>The search type's label, such as "Freeleech"; null for every torrent.</summary>
    public string? SearchTypeLabel =>
        SearchType == SearchQueryBuilder.DefaultType ? null : SearchQueryBuilder.TypeOptions.First(o => o.Value == SearchType).Label;

    /// <summary>The heading without search text: "Newest on MAM", "On MAM" when sorted otherwise, or "Freeleech on MAM".</summary>
    public string BrowseHeading =>
        SearchTypeLabel is { } type ? $"{type} on MAM"
        : SearchSort is SearchQueryBuilder.DefaultSort or "newest" ? "Newest on MAM"
        : "On MAM";

    /// <summary>How many torrents MAM found for the search, over all pages; null until the results are in.</summary>
    public int? Found { get; private set; }

    /// <summary>Results on this page left out because the account snatched them, with the "not snatched" type.</summary>
    public int SnatchedLeftOut { get; private set; }

    public long FirstResultNumber => Choices.FirstResultNumber;

    public bool HasNextPage => Found is { } found && Choices.HasPageAfter(found);

    /// <summary>
    /// The search as query-string values, with <paramref name="pageNumber"/> as the page (this page when unset), for
    /// links, the results request and the redirect after a download. Defaults are left out, so a plain search keeps a
    /// plain address.
    /// </summary>
    public RouteValueDictionary SearchRouteValues(int? pageNumber = null)
    {
        pageNumber ??= Choices.PageNumber;
        var values = new RouteValueDictionary();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            values[nameof(Q)] = Q;
        }

        if (!string.IsNullOrEmpty(Lang))
        {
            values[nameof(Lang)] = Lang;
        }

        if (!SearchFields.SequenceEqual(SearchQueryBuilder.DefaultFields))
        {
            values[nameof(In)] = SearchFields.ToArray();
        }

        if (SearchSort != SearchQueryBuilder.DefaultSort)
        {
            values[nameof(Sort)] = SearchSort;
        }

        if (SearchType != SearchQueryBuilder.DefaultType)
        {
            values[nameof(Type)] = SearchType;
        }

        if (pageNumber > 1)
        {
            values[nameof(PageNumber)] = pageNumber;
        }

        return values;
    }

    /// <summary>The query-string names that make up a search, carried through a result's Download and Import forms.</summary>
    public static IReadOnlySet<string> SearchStateKeys { get; } =
        new HashSet<string>([nameof(Q), nameof(Lang), nameof(In), nameof(Sort), nameof(Type), nameof(PageNumber)], StringComparer.OrdinalIgnoreCase);

    /// <summary>The cookie that remembers the category chosen for downloads, as people usually fill one library at a time.</summary>
    public const string CategoryCookie = "fs-search-category";

    /// <summary>The category last chosen for downloads; empty for the `[[tag]]` rules.</summary>
    public string RememberedCategory => Request.Cookies[CategoryCookie] ?? string.Empty;

    /// <summary>
    /// The category the dropdown starts at: the remembered one while it is still offered, else the `[[tag]]` rules, so a
    /// category that has gone (or can't be checked while qBittorrent doesn't answer) doesn't make every Download fail.
    /// </summary>
    public string ChosenCategory => CategoryOptions.Any(o => o.Value == RememberedCategory) ? RememberedCategory : string.Empty;

    /// <summary>
    /// The categories a download can go to: each library's, then any other qBittorrent has torrents in (once its list
    /// has been read). Empty means the `[[tag]]` rules decide.
    /// </summary>
    public IReadOnlyList<(string Value, string Label)> CategoryOptions { get; private set; } = [];

    private static IReadOnlyList<(string Value, string Label)> CategoriesFor(FetchshelfSettings settings, QBittorrentPresence presence)
    {
        var libraries = LibraryCategories.For(settings);
        var options = libraries.Select(c => (c.Category, c.Label)).ToList();
        if (presence.Loaded)
        {
            options.AddRange(presence.CategoryByHash.Values
                .Where(c => c.Length > 0 && libraries.All(l => l.Category != c))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(c => (c, c)));
        }

        return options;
    }

    /// <summary>The top-level <c>languages</c>, which a search uses unless <see cref="Lang"/> says otherwise. Validated when the config loads.</summary>
    public IReadOnlyList<Language> DefaultLanguages => settings.Languages
        .Select(name => LanguageNameResolver.TryResolve(name, out var language) ? language : (Language?)null)
        .OfType<Language>()
        .Distinct()
        .ToList();

    /// <summary>The languages this search is limited to; empty means any language.</summary>
    public IReadOnlyList<Language> SearchLanguages =>
        string.Equals(Lang, AnyLanguage, StringComparison.OrdinalIgnoreCase) ? []
        : Enum.TryParse<Language>(Lang, ignoreCase: true, out var language) && Enum.IsDefined(language) ? [language]
        : DefaultLanguages;

    /// <summary>The language dropdown: the default first, then any language when the default restricts it, then every language A-Z.</summary>
    public IReadOnlyList<(string Value, string Label)> LanguageOptions
    {
        get
        {
            var options = new List<(string Value, string Label)>();
            if (DefaultLanguages.Count > 0)
            {
                options.Add((string.Empty, $"Your languages ({string.Join(", ", DefaultLanguages.Select(LanguageNameResolver.DisplayName))})"));
                options.Add((AnyLanguage, "Any language"));
            }
            else
            {
                options.Add((string.Empty, "Any language"));
            }

            options.AddRange(Enum.GetValues<Language>()
                .Select(language => (language.ToString(), LanguageNameResolver.DisplayName(language)))
                .OrderBy(option => option.Item2, StringComparer.OrdinalIgnoreCase));
            return options;
        }
    }

    /// <summary>The dropdown value matching <see cref="Lang"/>, so the current choice stays selected.</summary>
    public string SelectedLanguageOption =>
        string.Equals(Lang, AnyLanguage, StringComparison.OrdinalIgnoreCase) ? (DefaultLanguages.Count > 0 ? AnyLanguage : string.Empty)
        : Enum.TryParse<Language>(Lang, ignoreCase: true, out var language) && Enum.IsDefined(language) ? language.ToString()
        : string.Empty;

    public IReadOnlyList<SearchResultViewModel> Results { get; private set; } = [];

    /// <summary>False while the page shell is rendered; the results arrive afterwards via <see cref="OnGetResultsAsync"/>.</summary>
    public bool ResultsLoaded { get; private set; }

    /// <summary>Set when the MAM search failed, so the results panel can say why instead of staying empty.</summary>
    public string? SearchError { get; private set; }

    /// <summary>
    /// Renders the page shell without waiting on MAM; the browser then fetches the table through
    /// <see cref="OnGetResultsAsync"/> (htmx, on load) while a spinner covers it. Reading qBittorrent's torrent
    /// list starts here, so it is usually ready by the time the results are.
    /// </summary>
    public void OnGet() => qbittorrentPresence.Warm();

    /// <summary>The results table for <see cref="Q"/>, swapped into the Search page by htmx.</summary>
    public async Task<PartialViewResult> OnGetResultsAsync()
    {
        // Neither the Ebooks/Audiobooks "Show" filter nor "Hide downloaded torrents" narrows this
        // query or the Results list - both are applied browser-side (see the page's script)
        // against each row's data-media-type/data-status attribute, so every result is always
        // fetched and rendered regardless of the current filter state.
        var query = SearchQueryBuilder.Build(Choices, SearchLanguages);

        try
        {
            // Read alongside the MAM search, not after it, in case the page's own warm-up hasn't finished.
            var presenceTask = qbittorrentPresence.GetAsync();
            var result = await mamClient.SearchAsync(query);
            var wedgeOverBytes = settings.Search.WedgeOverBytes;
            var mamIds = result.Data.Select(record => record.MamId).ToList();
            var presence = await presenceTask;
            CategoryOptions = CategoriesFor(settings, presence);
            var statusByMamId = await LoadStatusesAsync(mamIds, presence.Loaded ? presence.StateByHash : null);
            var hashesByMamId = await KnownHashesAsync(mamIds);
            Found = result.Found;

            // MAM can't be asked for torrents the account hasn't snatched, so those it has are left out of this page.
            var records = result.Data.ToList();
            if (SearchType == SearchQueryBuilder.NotSnatchedType)
            {
                SnatchedLeftOut = records.RemoveAll(record => record.IsSnatchedByMe);
            }

            Results = records
                .Select(record => WithQBittorrent(
                    SearchResultViewModel.From(
                        record, MamSizeParser.ParseBytes(record.SizeRaw), wedgeOverBytes,
                        statusByMamId.GetValueOrDefault(record.MamId, MamResultStatus.None), formatPreferenceLists),
                    presence, hashesByMamId.GetValueOrDefault(record.MamId) ?? []))
                .ToList();
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            // htmx does not swap error responses, so a thrown exception would leave the panel blank: say what went wrong instead.
            logger.LogWarning(ex, "Search on MAM failed");
            SearchError = MamErrorDescriber.Describe(ex, mamOptions.Timeout);
        }

        ResultsLoaded = true;
        return Partial("_SearchResultsPartial", this);
    }

    /// <summary>
    /// A result's status badge, asked for again by the badge itself while the torrent is on its way into the library.
    /// Reads Fetchshelf's database only: no MAM request, and no qBittorrent request either.
    /// </summary>
    public async Task<PartialViewResult> OnGetStatusAsync(long mamId, int round)
    {
        await RefreshDownloadingAsync(mamId);
        var status = (await LoadStatusesAsync([mamId])).GetValueOrDefault(mamId, MamResultStatus.None);
        return Partial("_SearchResultStatusPartial", new SearchResultStatusViewModel(mamId, status, round));
    }

    /// <summary>
    /// Asks qBittorrent about this MAM id's torrents that Fetchshelf still has as downloading, one torrent at a time (not
    /// the whole list), and stores their state. When one has finished, the Link job starts straight away instead of at
    /// its next interval, so the status moves on to Linked within seconds. If qBittorrent can't be asked, the stored
    /// status stands.
    /// </summary>
    private async Task RefreshDownloadingAsync(long mamId)
    {
        var downloading = (await dbContext.Torrents.Where(t => t.MamId == mamId && t.LibraryPath == null).ToListAsync())
            .Where(t => !MetadataOnlyTorrentId.IsMetadataOnly(t.Id) && (t.ClientStatus is null || QBittorrentStateLabel.IsDownloadStage(t.ClientStatus)))
            .ToList();
        if (downloading.Count == 0 || settings.QBittorrent.Count == 0)
        {
            return;
        }

        var finished = false;
        try
        {
            var clients = QBittorrentConnections.Build(settings, qbittorrentFactory).Select(c => c.Client).ToList();
            foreach (var torrent in downloading)
            {
                foreach (var client in clients)
                {
                    if (await client.GetTorrentAsync(torrent.Id, HttpContext.RequestAborted) is { } live)
                    {
                        ClientStatuses.Update(torrent, live.State);
                        finished |= live.IsFinished;
                        break;
                    }
                }
            }

            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogDebug("Could not ask qBittorrent about MAM torrent {MamId}: {Error}", mamId, ex.Message);
        }

        if (finished && jobRegistry.Get(JobNames.Link) is not null)
        {
            _ = jobRegistry.TriggerAsync(JobNames.Link, JobTrigger.Manual, CancellationToken.None);
        }
    }

    /// <summary>Whether qBittorrent has the torrent, and in which category: by MAM id, or by a known info hash.</summary>
    private static SearchResultViewModel WithQBittorrent(SearchResultViewModel row, QBittorrentPresence presence, IEnumerable<string> knownHashes)
    {
        if (!presence.Loaded)
        {
            return row;
        }

        var category = presence.CategoryOf(row.MamId, knownHashes);
        return row with { InQBittorrent = category is not null, QBittorrentCategory = category };
    }

    private async Task<Dictionary<long, List<string>>> KnownHashesAsync(List<long> mamIds) =>
        (await dbContext.Torrents.Where(t => mamIds.Contains(t.MamId)).Select(t => new { t.MamId, t.Id }).ToListAsync())
            .GroupBy(t => t.MamId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Id).ToList());

    /// <summary>
    /// Resolves each given MAM id to where it stands in Fetchshelf's pipeline: linked into the
    /// library, downloaded but not yet linked, or still queued as a selection.
    /// </summary>
    /// <param name="mamIds">The MAM ids to look up.</param>
    /// <param name="liveStates">
    /// qBittorrent's current state by info hash, when the caller has it: a torrent stored as downloading that qBittorrent
    /// has finished then shows as downloaded, without waiting for the Link job to store the state.
    /// </param>
    private async Task<Dictionary<long, MamResultStatus>> LoadStatusesAsync(List<long> mamIds, IReadOnlyDictionary<string, string>? liveStates = null)
    {
        var statuses = new Dictionary<long, MamResultStatus>();
        if (mamIds.Count == 0)
        {
            return statuses;
        }

        var selectedMamIds = await dbContext.SelectedTorrents
            .Where(s => s.RemovedAt == null && s.StartedAt == null && mamIds.Contains(s.MamId))
            .Select(s => s.MamId)
            .ToListAsync();
        foreach (var mamId in selectedMamIds)
        {
            statuses[mamId] = MamResultStatus.Selected;
        }

        var torrents = await dbContext.Torrents
            .Where(t => mamIds.Contains(t.MamId))
            .Select(t => new { t.Id, t.MamId, t.LibraryPath, t.ClientStatus })
            .ToListAsync();
        foreach (var torrent in torrents)
        {
            // The Download job records a torrent as it hands it to qBittorrent, with no client status yet; the Link job
            // stores the status once it sees the torrent finished. Until then it is still downloading. A metadata-only
            // record was never downloaded through Fetchshelf, so it counts as owned.
            var clientStatus = liveStates?.GetValueOrDefault(torrent.Id) ?? torrent.ClientStatus;
            var status = torrent.LibraryPath is not null ? MamResultStatus.Linked
                : !MetadataOnlyTorrentId.IsMetadataOnly(torrent.Id)
                    && (clientStatus is null || QBittorrentStateLabel.IsDownloadStage(clientStatus)) ? MamResultStatus.Downloading
                : MamResultStatus.Downloaded;
            if (status > statuses.GetValueOrDefault(torrent.MamId))
            {
                statuses[torrent.MamId] = status;
            }
        }

        return statuses;
    }

    /// <summary>
    /// Starts the selection for <paramref name="mamId"/>. From the Search page's htmx forms it answers with just the
    /// outcome message and the refreshed row, so the page is not reloaded; without htmx it redirects back to the page.
    /// </summary>
    /// <param name="category">
    /// The qBittorrent category chosen on the page, instead of the one the `[[tag]]` rules give; empty for the rules. A
    /// post without the field (a form sent without htmx) uses the remembered choice, and leaves it remembered. It must
    /// be one the page offers (<see cref="CategoriesFor"/>), so a stale cookie or a hand-made post can't create one.
    /// </param>
    public async Task<IActionResult> OnPostDownloadAsync(long mamId, bool wedge, string? category)
    {
        MamTorrentRecord? record = null;
        var failed = false;
        string message;
        var chosenHere = Request.HasFormContentType && Request.Form.ContainsKey(nameof(category));
        category = chosenHere ? category : RememberedCategory;
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category is not null && !CategoriesFor(settings, await qbittorrentPresence.GetAsync()).Any(o => o.Value == category))
        {
            message = $"Not downloaded: {category} is neither a library's category nor one qBittorrent has now. Choose another category.";
            if (!Request.Headers.ContainsKey("HX-Request"))
            {
                DownloadMessage = message;
                return RedirectToPage(SearchRouteValues());
            }

            return Partial("_DownloadFeedbackPartial", new DownloadFeedbackViewModel(message, null, IsError: true));
        }

        if (chosenHere)
        {
            RememberCategory(category);
        }

        try
        {
            var lookup = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnDownloadLink = true });
            record = lookup.Data.FirstOrDefault();
            if (record is not null)
            {
                var metadata = MamTorrentMapper.Map(record);
                var cost = wedge ? ConfiguredCost.Wedge : ConfiguredCost.Ratio;
                var outcome = await selectionRunner.ProcessCandidateAsync(
                    record, metadata, [], cost, autograbCategory: category, grabberName: JobNames.ManualSearchGrabber);
                logger.LogInformation("Manual download of {Torrent}: {Outcome}", metadata.DisplayName(), outcome.Describe(dryRun: false));
                var downloadStarted = outcome == SelectionOutcome.Selected && StartDownload();
                message = SelectionOutcomeMessages.Describe(outcome, metadata, formatPreferenceLists, downloadStarted);
                if (outcome == SelectionOutcome.Selected && category is not null)
                {
                    message += $" Category: {category}.";
                }
            }
            else
            {
                message = $"MAM returned no torrent with id {mamId}.";
            }
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogError(
                "Manual download of {Torrent} failed: {Error}",
                record is null ? $"MAM id {mamId}" : MamTorrentMapper.Map(record).DisplayName(), ex.Message);
            failed = true;
            record = null;
            message = $"Download failed: {MamErrorDescriber.Describe(ex, mamOptions.Timeout)}";
        }

        if (!Request.Headers.ContainsKey("HX-Request"))
        {
            DownloadMessage = message;
            return RedirectToPage(SearchRouteValues());
        }

        // Set only on the redirect path above: TempData would otherwise show this message again on the next page load.
        var row = record is null ? null : await BuildRowAsync(record, withQBittorrent: false);
        return Partial("_DownloadFeedbackPartial", new DownloadFeedbackViewModel(message, row, IsError: failed));
    }

    private void RememberCategory(string? category)
    {
        if (category is null)
        {
            Response.Cookies.Delete(CategoryCookie, new CookieOptions { Path = "/" });
            return;
        }

        Response.Cookies.Append(CategoryCookie, category, new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
    }

    /// <summary>
    /// Records a torrent qBittorrent already has, with MAM's details, instead of downloading it: it then shows on the
    /// Torrents page, and the Link job links it once a library accepts it. Its category in qBittorrent stays as it is.
    /// </summary>
    public async Task<IActionResult> OnPostImportAsync(long mamId)
    {
        MamTorrentRecord? record = null;
        var failed = false;
        string message;
        try
        {
            var presence = await qbittorrentPresence.GetAsync();
            var hash = presence.HashByMamId.GetValueOrDefault(mamId);
            record = (await mamClient.SearchAsync(new MamSearchQuery { Id = mamId })).Data.FirstOrDefault();
            if (record is null)
            {
                message = $"MAM returned no torrent with id {mamId}.";
            }
            else if (hash is null)
            {
                message = $"\"{record.Title}\" was not imported: qBittorrent doesn't have it (any more).";
            }
            else
            {
                message = await ImportAsync(
                    record, hash, presence.CategoryByMamId.GetValueOrDefault(mamId) ?? string.Empty, presence.StateByHash.GetValueOrDefault(hash));
            }
        }
        catch (Exception ex) when (!IsRequestAborted(ex))
        {
            logger.LogError("Import of MAM torrent {MamId} failed: {Error}", mamId, ex.Message);
            failed = true;
            record = null;
            message = $"Import failed: {MamErrorDescriber.Describe(ex, mamOptions.Timeout)}";
        }

        if (!Request.Headers.ContainsKey("HX-Request"))
        {
            DownloadMessage = message;
            return RedirectToPage(SearchRouteValues());
        }

        var row = record is null ? null : await BuildRowAsync(record, withQBittorrent: true);
        return Partial("_DownloadFeedbackPartial", new DownloadFeedbackViewModel(message, row, IsError: failed));
    }

    private async Task<string> ImportAsync(MamTorrentRecord record, string hash, string category, string? clientStatus)
    {
        var metadata = MamTorrentMapper.Map(record);
        var title = $"\"{metadata.Title}\"";
        if (await dbContext.Torrents.AnyAsync(t => t.Id == hash))
        {
            return $"{title} is already in Fetchshelf.";
        }

        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = hash,
            MamId = metadata.MamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            QBittorrentCategory = category,
            ClientStatus = clientStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // A queued selection of the same torrent has nothing left to do.
        foreach (var selected in await dbContext.SelectedTorrents.Where(s => s.MamId == metadata.MamId && s.RemovedAt == null && s.StartedAt == null).ToListAsync())
        {
            selected.RemovedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Imported {Torrent} from qBittorrent (category {Category})", metadata.DisplayName(), category.Length == 0 ? "none" : category);
        return LibraryCategories.IsLibraryCategory(settings, category)
            ? $"Imported {title}; the next Link run links it."
            : $"Imported {title}. None of your libraries uses its qBittorrent category ({(category.Length == 0 ? "none" : category)}). "
                + "Unless a library picks it up by its save path, give it a library's category with Set category on the Torrents page.";
    }

    /// <summary>A result row as it stands now, for swapping into the page after Download or Import.</summary>
    private async Task<SearchResultViewModel> BuildRowAsync(MamTorrentRecord record, bool withQBittorrent)
    {
        var wedgeOverBytes = settings.Search.WedgeOverBytes;
        var statuses = await LoadStatusesAsync([record.MamId]);
        var row = SearchResultViewModel.From(
            record, MamSizeParser.ParseBytes(record.SizeRaw), wedgeOverBytes,
            statuses.GetValueOrDefault(record.MamId, MamResultStatus.None), formatPreferenceLists);
        return withQBittorrent
            ? WithQBittorrent(row, await qbittorrentPresence.GetAsync(), (await KnownHashesAsync([record.MamId])).GetValueOrDefault(record.MamId) ?? [])
            : row;
    }

    /// <summary>
    /// Starts the Download job for a new selection, unless `download_after_selection` is off or no jobs run. It runs
    /// in the background, like Run now on the Jobs page, so the page answers at once.
    /// </summary>
    /// <summary>
    /// Starts the Download job for a selection just made, or queues one more run if it is busy, so the selection isn't
    /// left for the next scheduled run. Doesn't wait for it: the row's status follows the torrent from there.
    /// </summary>
    private bool StartDownload()
    {
        if (!settings.DownloadAfterSelection || jobRegistry.Get(JobNames.Download) is null)
        {
            return false;
        }

        _ = jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Manual, CancellationToken.None);
        return true;
    }

    /// <summary>True when the browser gave up on the request: nobody is left to show an error to, so let it propagate.</summary>
    private bool IsRequestAborted(Exception exception) =>
        exception is OperationCanceledException && HttpContext.RequestAborted.IsCancellationRequested;
}
