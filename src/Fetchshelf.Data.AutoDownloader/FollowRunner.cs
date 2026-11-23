using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one run of the Follow job did.</summary>
/// <param name="Searched">Followed series and authors searched on MAM.</param>
/// <param name="Selected">Torrents selected; in a dry run, those that would have been.</param>
/// <param name="NewFinds">Torrents found for watched series and authors, now listed on the Following page.</param>
/// <param name="Failed">Follows whose search failed.</param>
/// <param name="DryRun">Whether the run was a dry run, which selects and writes nothing.</param>
/// <param name="AddedFromAudiobookshelf">Series from Audiobookshelf followed for the first time (`audiobookshelf_series`).</param>
public sealed record FollowRunSummary(int Searched, int Selected, int NewFinds, int Failed, bool DryRun = false, int AddedFromAudiobookshelf = 0)
{
    /// <summary>The selections now waiting to be downloaded: none in a dry run.</summary>
    public int SelectedForDownload => DryRun ? 0 : Selected;
}

/// <summary>
/// The Follow job: searches MAM for the followed series and authors that are due, and selects what the library is
/// missing (<see cref="FollowMode.Download"/>) or lists it on the Following page (<see cref="FollowMode.Watch"/>).
/// </summary>
/// <remarks>
/// One MAM search per follow, at most <see cref="FollowSettings.SearchesPerRun"/> per run, and each follow at most once
/// per <see cref="FollowSettings.SearchAgainAfter"/> hours. A search reads its results newest first, page by page: every
/// page the first time and every <see cref="ReadAllAgainAfter"/> after that, and otherwise only until it reaches torrents
/// uploaded before the previous search, which that search saw. MAM's search matches words, so a result counts only when its
/// series (or author) has the followed name's key, the same key the library's series and people rows have. What the
/// library already has, or the selection queue holds, is never a find.
/// </remarks>
/// <summary>What the Follow job takes from outside its section.</summary>
/// <param name="FirstLanguage">
/// The first of the top-level `languages`: the language of a find recorded before languages were kept. Null when none
/// is configured; such a find is then checked against the library in every language.
/// </param>
public sealed record FollowRunnerOptions(Language? FirstLanguage);

public sealed class FollowRunner(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    LibraryQueries libraryQueries,
    FollowSettings settings,
    FormatPreferenceLists preferenceLists,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger<FollowRunner>? logger = null,
    AudiobookshelfSeriesSync? audiobookshelfSeries = null,
    FollowRunnerOptions? options = null)
{
    private readonly Language? _firstLanguage = options?.FirstLanguage;

    /// <summary>Whether this run is a dry run: the configured `dry_run`, or the Jobs page's <b>Dry run</b>. Set at the start of a run.</summary>
    private bool _dryRun;

    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Torrents per page of a follow's search, the most MAM gives at once.</summary>
    public const int PageSize = 100;

    /// <summary>
    /// The most pages one follow's search reads, 10,000 torrents: a guard against a search that never ends, not a limit a
    /// real series or author reaches.
    /// </summary>
    public const int MaxPages = 100;

    /// <summary>
    /// How often a follow's search reads every page again, not only what was uploaded since: an older torrent can be
    /// given the series or author later, or become free.
    /// </summary>
    public static readonly TimeSpan ReadAllAgainAfter = TimeSpan.FromDays(30);

    /// <summary>
    /// How far before the previous search a later one reads back: a torrent can show up on MAM a while after its upload
    /// time, such as one that was waiting for a moderator.
    /// </summary>
    public static readonly TimeSpan ReadBackOverlap = TimeSpan.FromDays(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger<FollowRunner> _logger = logger ?? NullLogger<FollowRunner>.Instance;

    /// <summary>The grabber name the job's selections carry, so the Selected page says which follow chose them.</summary>
    /// <summary>What every grabber name of the Follow job starts with, which `[follow]`'s own buffers are found by.</summary>
    public const string GrabberPrefix = "Follow: ";

    public static string GrabberName(FollowEntity follow) => GrabberPrefix + follow.DisplayName;

    /// <remarks>
    /// The free unsatisfied-snatch slots don't limit what a run selects, unlike an autograb's: a follow's selections are
    /// one series' or author's missing books, and the Download job checks the slots, `unsat_buffer`, ratio and wedges
    /// before each download, so what it can't start yet waits on the Selected page. Only `max_active_downloads` caps them.
    /// </remarks>
    public async Task<FollowRunSummary> RunAsync(
        IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken = default, bool? dryRun = null)
    {
        _dryRun = dryRun ?? settings.DryRun;
        var added = await SyncAudiobookshelfSeriesAsync(cancellationToken);
        var now = _time.GetUtcNow();
        var searchAgainAfter = TimeSpan.FromHours(settings.SearchAgainAfter);

        // A few hundred rows at most; DateTimeOffset comparisons don't translate to SQLite anyway.
        var due = (await dbContext.Follows.ToListAsync(cancellationToken))
            .Where(f => f.Mode != FollowMode.Ignore && (f.LastSearchedAt is null || f.LastSearchedAt <= now - searchAgainAfter))
            .OrderBy(f => f.LastSearchedAt ?? DateTimeOffset.MinValue)
            .ThenBy(f => f.Id)
            .Take(settings.SearchesPerRun)
            .ToList();
        if (_dryRun)
        {
            _logger.LogInformation("Dry run, nothing will be selected or written");
        }

        await RetireForFollowsNotDueAsync(due, cancellationToken);

        if (due.Count == 0)
        {
            _logger.LogInformation("No series or author on the Following page is due for a search");
            return new FollowRunSummary(0, 0, 0, 0, _dryRun, added);
        }

        using var _ = await accessGate.AcquireAsync(JobNames.Follow, cancellationToken);

        // `max_active_downloads` caps every "Follow: …" selection waiting, together; without it a run selects all it finds.
        var budget = int.MaxValue;
        if (settings.MaxActiveDownloads is { } cap)
        {
            var waiting = await SelectionQueue.ActiveForGrabberAsync(dbContext, GrabberPrefix, isPrefix: true, cancellationToken);
            budget = (int)Math.Max(0, cap - waiting);
            if (budget == 0 && due.All(f => f.Mode == FollowMode.Download))
            {
                _logger.LogInformation(
                    "Nothing searched, max_active_downloads {Cap} of Follow's selections are already waiting to be downloaded", cap);
                return new FollowRunSummary(0, 0, 0, 0, _dryRun, added);
            }
        }

        var grabFilters = settings.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var run = new RunState(budget);
        var errors = await ItemErrors.LoadAsync(dbContext, ErrorStage.Follow, _dryRun, cancellationToken);
        foreach (var follow in due)
        {
            if (follow.Mode == FollowMode.Download && run.BudgetLeft <= 0)
            {
                // Watched follows select nothing, so they are still searched.
                continue;
            }

            // A failed search counts as searched too; the Errors page shows it until a search succeeds.
            var failure = await errors.RunAsync(
                ErrorLog.FollowKey(follow.Id), follow.DisplayName,
                () => ProcessFollowAsync(follow, grabFilters, ignoreTorrents, run, cancellationToken),
                markFailed: () => follow.LastSearchedAt = _time.GetUtcNow(),
                cancellationToken);
            if (failure is not null)
            {
                run.Failed++;
                _logger.LogWarning(failure, "Searching for {Name} failed: {Reason}", follow.DisplayName, failure.Message);
            }
        }

        if (_dryRun)
        {
            dbContext.DiscardPendingChanges();
        }

        _logger.LogInformation(
            "{Searched} series and authors searched on MAM, {Selected} {Verb}, {New} new to look at{Failures}",
            run.Searched, run.Selected, _dryRun ? "would have been selected (dry run)" : "selected", run.NewFinds,
            run.Failed == 0 ? string.Empty : $", {run.Failed} failed");
        return new FollowRunSummary(run.Searched, run.Selected, run.NewFinds, run.Failed, _dryRun, added);
    }

    /// <summary>
    /// Takes off the To look at lists of the follows not searched in this run what has reached the library or the selection
    /// queue since their last search, by any route: another follow, an autograb, a list or the Search page. The follows
    /// searched in this run do the same as part of their search. Only the database is read; nothing is asked of MAM.
    /// </summary>
    private async Task RetireForFollowsNotDueAsync(IReadOnlyList<FollowEntity> due, CancellationToken cancellationToken)
    {
        var dueIds = due.Select(f => f.Id).ToHashSet();
        var waiting = await dbContext.FollowFinds
            .Where(f => f.Status == FollowFindStatus.New || f.Status == FollowFindStatus.Wanted)
            .Join(dbContext.Follows.Where(f => f.Mode != FollowMode.Ignore), find => find.FollowId, follow => follow.Id, (find, follow) => find)
            .ToListAsync(cancellationToken);
        var byFollow = waiting.Where(f => !dueIds.Contains(f.FollowId)).GroupBy(f => f.FollowId).ToList();
        if (byFollow.Count == 0)
        {
            return;
        }

        var follows = await dbContext.Follows.Where(f => byFollow.Select(g => g.Key).Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        foreach (var finds in byFollow)
        {
            await RetireCoveredFindsAsync(follows[finds.Key], finds.ToDictionary(f => f.MamId), cancellationToken);
        }

        if (!_dryRun)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// With `audiobookshelf_series`, follows Audiobookshelf's new series before the due follows are chosen, so they can be
    /// searched in this run. Audiobookshelf failing doesn't stop the run: the follows already there are searched as usual.
    /// </summary>
    private async Task<int> SyncAudiobookshelfSeriesAsync(CancellationToken cancellationToken)
    {
        if (audiobookshelfSeries is null)
        {
            return 0;
        }

        try
        {
            return (await audiobookshelfSeries.SyncAsync(_dryRun, cancellationToken)).Added;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "The series could not be read from Audiobookshelf, so the run searches the follows it has: {Error}", ex.Message);
            return 0;
        }
    }

    private sealed class RunState(int budget)
    {
        public int BudgetLeft { get; set; } = budget;

        public int Searched { get; set; }

        public int Selected { get; set; }

        public int NewFinds { get; set; }

        public int Failed { get; set; }
    }

    /// <summary>
    /// Where one follow's search results went, for the log: a search that finds 90 torrents and lists nothing new says
    /// why, such as an owned collection that holds most of a series.
    /// </summary>
    /// <param name="Results">Torrents MAM returned.</param>
    /// <param name="Pages">The pages they were read in.</param>
    /// <param name="OfFollow">Of those, ebooks and audiobooks of the series, or by the author.</param>
    /// <param name="Owned">Of those, what the library or the selection queue has: the torrent, the book, or its series entry.</param>
    /// <param name="Left">The others.</param>
    /// <param name="Books">The books and formats those are: one torrent each is looked at.</param>
    /// <param name="Taken">The books and formats a grab block's filter takes.</param>
    private sealed record SearchOutcome(int Results, int Pages, int OfFollow, int Owned, int Left, int Books, int Taken)
    {
        /// <summary>Already listed, dismissed or selected by an earlier search.</summary>
        public int Known { get; set; }

        public int New { get; set; }

        public int Selected { get; set; }

        public int NotFree { get; set; }

        /// <summary>Taken, but not selected, such as a duplicate of a selected copy.</summary>
        public int NotSelected { get; set; }

        public string Describe(FollowKind kind)
        {
            var whose = kind == FollowKind.Series ? "in the series" : "by the author";
            var notTaken = Books - Taken;
            return $"{Results} torrents on MAM in {Pages} {(Pages == 1 ? "page" : "pages")}, {OfFollow} of them ebooks or audiobooks {whose}; {Owned} already in the library or selected; "
                + $"the other {Left} are {Books} books (one torrent per book, language and format)"
                + (notTaken > 0 ? $", {notTaken} of them in no grab block's filter" : string.Empty)
                + $". Of those, {Known} found before, {New} new to look at, {Selected} selected, {NotFree} not free"
                + (NotSelected > 0 ? $", {NotSelected} not selected" : string.Empty);
        }
    }

    /// <summary>A search result that is a book of the followed series or author.</summary>
    private sealed record Candidate(
        MamTorrentRecord Record, TorrentMetadata Metadata, FilterMainType Side, string BookKey, string? SeriesDisplay, double? SeriesNumber,
        double? SeriesLastNumber = null);

    /// <summary>A result whose book is already in the library (<paramref name="Owned"/>) or selected; <paramref name="InLibrary"/> when this very torrent is.</summary>
    private sealed record HadCandidate(Candidate Candidate, bool Owned, bool InLibrary, bool Queued);

    /// <summary>What to do about one book in one format: take the winner with its block's cost, or note the match no block pays for.</summary>
    private sealed record Decision(Candidate? Winner, GrabEntry? Grab, Candidate? TooExpensive)
    {
        public Candidate Shown => Winner ?? TooExpensive!;
    }

    private async Task ProcessFollowAsync(
        FollowEntity follow, IReadOnlyList<TorrentFilter> grabFilters, IReadOnlyList<long> ignoreTorrents, RunState run,
        CancellationToken cancellationToken)
    {
        var (records, readAll, pages) = await SearchPagesAsync(follow, grabFilters, cancellationToken);
        run.Searched++;

        var candidates = records.Select(record => ToCandidate(follow, record)).OfType<Candidate>().ToList();
        var ofFollow = candidates.Count;
        var finds = await dbContext.FollowFinds.Where(f => f.FollowId == follow.Id).ToDictionaryAsync(f => f.MamId, cancellationToken);

        // Finds already listed are mostly left as they are below, so their sizes are brought up to date here, from every
        // result: also those that are owned by now, or dismissed. So is a missing language.
        foreach (var record in records)
        {
            if (finds.TryGetValue(record.MamId, out var listed) && SizeOf(record) is { } size)
            {
                listed.SizeBytes = size;
            }
        }

        // A find recorded before languages were kept gets its language the first time a search sees it again.
        foreach (var candidate in candidates.Where(c => finds.TryGetValue(c.Record.MamId, out var listed) && listed.Language is null))
        {
            finds[candidate.Record.MamId].Language = candidate.Metadata.Language.ToString();
        }
        (candidates, var had) = await WithoutOwnedAsync(follow, candidates, finds, cancellationToken);
        await RetireCoveredFindsAsync(follow, finds, cancellationToken);
        TidyDuplicateFinds(follow, finds);
        var findsByBook = finds.Values.GroupBy(f => (BookKeyOf(follow, f), f.MediaSide)).ToDictionary(g => g.Key, g => g.First());

        var decisions = Decide(follow, candidates, grabFilters, out var books);
        var now = _time.GetUtcNow();
        var foundSomething = false;
        var stoppedByBudget = false;
        var outcome = new SearchOutcome(records.Count, pages, ofFollow, ofFollow - candidates.Count, candidates.Count, books, decisions.Count);
        foreach (var decision in decisions)
        {
            var shown = decision.Shown;
            // Another upload of a book and format already listed is that book's find, not a second one.
            var known = finds.GetValueOrDefault(shown.Record.MamId) ?? findsByBook.GetValueOrDefault((shown.BookKey, shown.Side.ToString()));
            if (known is not null
                && (known.Status is FollowFindStatus.Dismissed or FollowFindStatus.Selected
                    || (known.Status is FollowFindStatus.New && follow.Mode == FollowMode.Watch)
                    || (known.Status is FollowFindStatus.Wanted && decision.Winner is null)))
            {
                outcome.Known++;
                continue;
            }

            if (known is not null && known.MamId != shown.Record.MamId)
            {
                // The book's find moves on to this upload, the one the search now takes for the book and format.
                finds.Remove(known.MamId);
                (known.MamId, known.Title, known.SizeBytes) = (shown.Record.MamId, shown.Metadata.Title, SizeOf(shown.Record));
                known.Language = shown.Metadata.Language.ToString();
                finds[known.MamId] = known;
            }

            if (decision.Winner is null)
            {
                Record(follow, shown, FollowFindStatus.Wanted, finds, now);
                Log(follow, shown, "matches, but no grab block's cost accepts it");
                outcome.NotFree++;
                foundSomething = true;
                continue;
            }

            if (follow.Mode == FollowMode.Watch)
            {
                Record(follow, shown, FollowFindStatus.New, finds, now);
                run.NewFinds++;
                outcome.New++;
                foundSomething = true;
                Log(follow, shown, "listed on the Following page");
                continue;
            }

            if (run.BudgetLeft <= 0)
            {
                stoppedByBudget = true;
                _logger.LogInformation(
                    "Stopped at max_active_downloads, the most of Follow's selections that may wait; {Name} is searched again next run",
                    follow.DisplayName);
                break;
            }

            var selection = await selectionRunner.ProcessCandidateAsync(
                shown.Record, shown.Metadata, ignoreTorrents, decision.Grab!.Cost, placement: new DownloadPlacement(settings.Category, settings.Client, settings.SavePath),
                grabberName: GrabberName(follow), dryRun: _dryRun, cancellationToken: cancellationToken);
            Log(follow, shown, selection.Describe(_dryRun));
            switch (selection)
            {
                case SelectionOutcome.Selected or SelectionOutcome.SelectedAsUpgrade:
                    Record(follow, shown, FollowFindStatus.Selected, finds, now);
                    run.Selected++;
                    run.BudgetLeft--;
                    outcome.Selected++;
                    foundSomething = true;
                    break;
                case SelectionOutcome.CostNotFree:
                    Record(follow, shown, FollowFindStatus.Wanted, finds, now);
                    outcome.NotFree++;
                    foundSomething = true;
                    break;
                default:
                    outcome.NotSelected++;
                    break;
            }
        }

        RecordHad(follow, had, candidates, finds, now);
        _logger.LogInformation("{Name}: {Outcome}", follow.DisplayName, outcome.Describe(follow.Kind));

        if (!stoppedByBudget)
        {
            follow.LastSearchedAt = now;
            if (readAll)
            {
                follow.FullySearchedAt = now;
            }
        }

        if (foundSomething)
        {
            follow.LastFoundAt = now;
        }
    }

    private void Log(FollowEntity follow, Candidate candidate, string what) =>
        _logger.Log(
            _dryRun ? LogLevel.Information : LogLevel.Debug, "{Name}: \"{Title}\" (MAM torrent {MamId}): {What}",
            follow.DisplayName, candidate.Metadata.Title, candidate.Record.MamId, what);

    /// <summary>The torrent's size from MAM's listing; null when MAM gives none or one that can't be read, as it is only shown.</summary>
    private static long? SizeOf(MamTorrentRecord record)
    {
        try
        {
            return string.IsNullOrWhiteSpace(record.SizeRaw) ? null : MamSizeParser.ParseBytes(record.SizeRaw);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Adds or updates the follow's find for the torrent. In a dry run nothing is kept: the run discards it.</summary>
    private void Record(FollowEntity follow, Candidate candidate, FollowFindStatus status, Dictionary<long, FollowFindEntity> finds, DateTimeOffset now)
    {
        if (!finds.TryGetValue(candidate.Record.MamId, out var find))
        {
            find = new FollowFindEntity
            {
                FollowId = follow.Id,
                MamId = candidate.Record.MamId,
                Title = candidate.Metadata.Title,
                SeriesDisplay = candidate.SeriesDisplay,
                MediaSide = candidate.Side.ToString(),
                Language = candidate.Metadata.Language.ToString(),
                SizeBytes = SizeOf(candidate.Record),
                Status = status,
                FoundAt = now,
                StatusAt = now,
            };
            finds[find.MamId] = find;
            dbContext.FollowFinds.Add(find);
            return;
        }

        (find.Status, find.StatusAt) = (status, now);
        find.Language ??= candidate.Metadata.Language.ToString();
    }

    /// <summary>
    /// Reads the follow's results, newest first, page by page: all of them when the follow hasn't had every page read
    /// within <see cref="ReadAllAgainAfter"/>, else until a page reaches torrents uploaded before the previous search
    /// (less <see cref="ReadBackOverlap"/>). Returns them, each once, whether every page was read, and how many pages
    /// were.
    /// </summary>
    private async Task<(List<MamTorrentRecord> Records, bool ReadAll, int Pages)> SearchPagesAsync(
        FollowEntity follow, IReadOnlyList<TorrentFilter> grabFilters, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var readAll = follow.LastSearchedAt is null || follow.FullySearchedAt is not { } fully || fully <= now - ReadAllAgainAfter;
        DateTimeOffset? readBackTo = readAll ? null : follow.LastSearchedAt - ReadBackOverlap;

        var records = new List<MamTorrentRecord>();
        var seen = new HashSet<long>();
        for (var page = 0; page < MaxPages; page++)
        {
            var offset = page * (long)PageSize;
            var result = await mamClient.SearchAsync(ToSearchQuery(follow, grabFilters, offset), cancellationToken);
            await _delay(PolitenessDelay, cancellationToken);
            // A torrent uploaded while the pages are read pushes the others down a place: it is seen twice, kept once.
            var fresh = result.Data.Where(record => seen.Add(record.MamId)).ToList();
            records.AddRange(fresh);
            _logger.LogDebug(
                "{Name}: page {Page} from torrent {Offset}: {Count} torrents, {Fresh} not on an earlier page; MAM gave start {Start}, found {Found}, total {Total}",
                follow.DisplayName, page + 1, offset, result.Data.Count, fresh.Count, result.Start, result.Found, result.Total);

            // A short page is the last; so is a full one that reaches the count MAM gives, when it gives one. MAM gives
            // two, found and total; the larger is taken, so a count of the page alone doesn't end the search early.
            var count = Math.Max(result.Found, result.Total);
            if (result.Data.Count < PageSize || (count > 0 && offset + PageSize >= count))
            {
                return (records, readAll, page + 1);
            }

            if (fresh.Count == 0)
            {
                // The same torrents again: MAM didn't move on to the next page, and reading on would only repeat them.
                _logger.LogWarning(
                    "{Name}: MAM gave page {Page} (from torrent {Offset}) only torrents an earlier page had, so the search stops at {Count} torrents. MAM gave start {Start}, found {Found}, total {Total}",
                    follow.DisplayName, page + 1, offset, records.Count, result.Start, result.Found, result.Total);
                return (records, false, page + 1);
            }

            if (readBackTo is { } since && result.Data.Min(record => record.UploadedAt) < since)
            {
                _logger.LogDebug(
                    "{Name}: read {Pages} pages, back to torrents the last search saw", follow.DisplayName, page + 1);
                return (records, false, page + 1);
            }
        }

        _logger.LogWarning(
            "{Name}: stopped after {Pages} pages of results ({Count} torrents); older ones are not looked at",
            follow.DisplayName, MaxPages, records.Count);
        return (records, false, MaxPages);
    }

    /// <summary>
    /// The search: the followed name in MAM's series (or author) field, newest first, so a series or author with more
    /// than one page of torrents still has its new ones seen. Limited to the languages and main categories every grab
    /// block together allows; each block's own filter is applied to the results afterwards.
    /// </summary>
    private static MamSearchQuery ToSearchQuery(FollowEntity follow, IReadOnlyList<TorrentFilter> grabFilters, long pageOffset)
    {
        var languages = grabFilters.Any(f => f.Languages is not { Count: > 0 })
            ? []
            : grabFilters.SelectMany(f => f.Languages!).Distinct().Select(l => (int)l).ToList();
        var mainCategories = grabFilters.Select(f => AutograbSearchTranslator.ToMainCategoryIds(f.Categories)).ToList();
        return new MamSearchQuery
        {
            Text = ListMamQueryBuilder.PlainWords(follow.DisplayName),
            SearchFields = [follow.Kind == FollowKind.Series ? MamSearchField.Series : MamSearchField.Author],
            Target = MamSearchTarget.New,
            SortOrder = MamSortOrder.NewestByDate,
            LanguageIds = languages,
            MainCategoryIds = mainCategories.Any(ids => ids.Count == 0) ? [] : mainCategories.SelectMany(ids => ids).Distinct().ToList(),
            PageOffset = pageOffset,
            PerPage = PageSize,
            ReturnDownloadLink = true,
        };
    }

    /// <summary>The result as a candidate when it really is a book of the followed series or author, as an audiobook or ebook.</summary>
    private static Candidate? ToCandidate(FollowEntity follow, MamTorrentRecord record)
    {
        if (GrabMatching.TryMap(record) is not { } metadata)
        {
            return null;
        }

        var side = FilterMainTypeMapper.FromMediaType(metadata.MediaType);
        if (side is not (FilterMainType.Audio or FilterMainType.Ebook))
        {
            return null;
        }

        // A book in two languages is two books: each language's edition is found, owned and decided on its own.
        var language = metadata.Language + "|";
        var projection = TorrentProjection.From(metadata);
        if (follow.Kind == FollowKind.Author)
        {
            return projection.People.Any(p => p.Role == PersonRole.Author && p.NameKey == follow.NameKey)
                ? new Candidate(record, metadata, side, language + "title:" + TitleNormalizer.Normalize(metadata.Title), null, null)
                : null;
        }

        var entry = projection.Series.FirstOrDefault(s => s.NameKey == follow.NameKey);
        return entry is null
            ? null
            : new Candidate(
                record, metadata, side,
                language + (entry.Display is null ? "title:" + TitleNormalizer.Normalize(metadata.Title) : "entry:" + entry.Display),
                entry.Display, entry.FirstNumber is { } number ? (double)number : null, entry.LastNumber is { } last ? (double)last : null);
    }

    /// <summary>
    /// Leaves out what the library or the selection queue has: the same MAM torrent, the same book (as selection judges
    /// it), or, for a series, an entry already owned in that format and language. A find for such a torrent is marked owned or
    /// selected, so the Following page stops offering it.
    /// </summary>
    private async Task<(List<Candidate> Kept, List<HadCandidate> Had)> WithoutOwnedAsync(
        FollowEntity follow, List<Candidate> candidates, Dictionary<long, FollowFindEntity> finds, CancellationToken cancellationToken)
    {
        var mamIds = candidates.Select(c => c.Record.MamId).ToList();
        var inLibrary = (await dbContext.Torrents.Where(t => mamIds.Contains(t.MamId)).Select(t => t.MamId).ToListAsync(cancellationToken)).ToHashSet();
        var queued = (await dbContext.SelectedTorrents.Where(s => s.RemovedAt == null && mamIds.Contains(s.MamId)).Select(s => s.MamId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var titleKeys = candidates.Select(c => TitleNormalizer.Normalize(c.Metadata.Title)).Where(k => k.Length > 0).Distinct().ToList();
        var sameTitle = (await dbContext.Torrents.AsNoTracking()
                .Where(t => titleKeys.Contains(t.NormalizedTitleKey) && t.ReplacedWithTorrentId == null)
                .Select(t => new { t.NormalizedTitleKey, t.MetadataJson })
                .ToListAsync(cancellationToken))
            .Select(t => (Key: t.NormalizedTitleKey, Metadata: TorrentMetadataSerializer.Deserialize(t.MetadataJson)))
            .ToList();

        // Per format and language: an entry owned in English doesn't make the Hungarian edition owned.
        var ownedNumbers = new Dictionary<(FilterMainType, Language), (HashSet<int> Covered, HashSet<string> Displays)>();
        if (follow.Kind == FollowKind.Series)
        {
            foreach (var (side, language) in candidates.Select(c => (c.Side, c.Metadata.Language)).Distinct())
            {
                var owned = await libraryQueries.OwnedEntriesAsync(follow.NameKey, side, language, cancellationToken);
                ownedNumbers[(side, language)] = (
                    SeriesGaps.Covered(owned.Where(e => e.FirstNumber is not null).Select(e => (e.FirstNumber!.Value, e.LastNumber ?? e.FirstNumber!.Value))),
                    owned.Select(e => e.Display).OfType<string>().ToHashSet(StringComparer.Ordinal));
            }
        }

        var kept = new List<Candidate>();
        var had = new List<HadCandidate>();
        foreach (var candidate in candidates)
        {
            var mamId = candidate.Record.MamId;
            var titleKey = TitleNormalizer.Normalize(candidate.Metadata.Title);
            var owned = inLibrary.Contains(mamId)
                || sameTitle.Any(t => t.Key == titleKey && SameBookComparer.AreSameBook(candidate.Metadata, t.Metadata))
                || (ownedNumbers.TryGetValue((candidate.Side, candidate.Metadata.Language), out var numbers)
                    && IsOwnedEntry(candidate.SeriesDisplay, candidate.SeriesNumber, candidate.SeriesLastNumber, numbers.Covered, numbers.Displays));
            if (owned || queued.Contains(mamId))
            {
                if (finds.TryGetValue(mamId, out var find) && find.Status is FollowFindStatus.New or FollowFindStatus.Wanted)
                {
                    (find.Status, find.StatusAt) = (owned ? FollowFindStatus.Owned : FollowFindStatus.Selected, _time.GetUtcNow());
                }

                had.Add(new HadCandidate(candidate, owned, inLibrary.Contains(mamId), queued.Contains(mamId)));
                continue;
            }

            kept.Add(candidate);
        }

        return (kept, had);
    }

    /// <summary>
    /// The books already in the library or selected get a find too, one per book, language and format as the others, so a
    /// follow's page lists the whole series or author and its In the library count says what is had: a series followed
    /// when most of it is in the library had only its missing books listed, and counted none in the library. A book
    /// with a find already (by this torrent or another of it), or among the ones still to decide, is left as it is. The
    /// torrent shown for a book is the one in the library, else the one selected, else the most seeded.
    /// </summary>
    private void RecordHad(
        FollowEntity follow, List<HadCandidate> had, List<Candidate> undecided, Dictionary<long, FollowFindEntity> finds, DateTimeOffset now)
    {
        var undecidedBooks = undecided.Select(c => (c.BookKey, c.Side)).ToHashSet();
        var listedBooks = finds.Values.Select(f => (TitleNormalizer.Normalize(f.Title), f.MediaSide, f.Language)).ToHashSet();
        foreach (var book in had.GroupBy(h => (h.Candidate.BookKey, h.Candidate.Side)))
        {
            if (undecidedBooks.Contains(book.Key) || book.Any(h => finds.ContainsKey(h.Candidate.Record.MamId)))
            {
                continue;
            }

            var shown = book.OrderByDescending(h => h.InLibrary).ThenByDescending(h => h.Queued).ThenByDescending(h => h.Candidate.Record.Seeders).First();
            var metadata = shown.Candidate.Metadata;
            if (listedBooks.Contains((TitleNormalizer.Normalize(metadata.Title), shown.Candidate.Side.ToString(), metadata.Language.ToString())))
            {
                continue;
            }

            Record(follow, shown.Candidate, book.Any(h => h.Owned) ? FollowFindStatus.Owned : FollowFindStatus.Selected, finds, now);
            listedBooks.Add((TitleNormalizer.Normalize(metadata.Title), shown.Candidate.Side.ToString(), metadata.Language.ToString()));
        }
    }

    private static bool IsOwnedEntry(string? display, double? firstNumber, double? lastNumber, HashSet<int> covered, HashSet<string> displays)
    {
        if (display is null)
        {
            return false;
        }

        if (displays.Contains(display))
        {
            return true;
        }

        // Owned when owned entries cover every book it holds: 2 within an owned 1-3, but not an omnibus 3-5 when only 3
        // is owned. An entry holding no whole book (1.5) is only owned under its own display.
        if (firstNumber is not { } first)
        {
            return false;
        }

        var books = SeriesGaps.Covered([(first, lastNumber ?? first)]);
        return books.Count > 0 && books.IsSubsetOf(covered);
    }

    /// <summary>A find's language: as recorded, else the first configured one; null for any (see <see cref="FollowRunnerOptions"/>).</summary>
    private Language? LanguageOf(FollowFindEntity find) =>
        Enum.TryParse<Language>(find.Language, out var language) && Enum.IsDefined(language) ? language : _firstLanguage;

    /// <summary>The key a search result of the find's book would have (see <c>ToCandidate</c>), from what the find stores.</summary>
    private string BookKeyOf(FollowEntity follow, FollowFindEntity find) =>
        (LanguageOf(find)?.ToString() ?? Language.English.ToString()) + "|"
        + (follow.Kind == FollowKind.Series && find.SeriesDisplay is not null ? "entry:" + find.SeriesDisplay : "title:" + TitleNormalizer.Normalize(find.Title));

    /// <summary>
    /// Finds still to look at, or not free, that the library or the selection queue has come to hold since: checked on
    /// every run from what the find stores, as a search returns a torrent again only while it is among the newest uploads,
    /// or at a full read every <see cref="ReadAllAgainAfter"/>. An old upload found before its book came into the library,
    /// such as by a collection, was otherwise offered for weeks. Owned as a search result is: the same torrent, a library
    /// torrent of the follow with the same title, format and language, or, for a series, an entry owned in that format
    /// and language.
    /// </summary>
    private async Task RetireCoveredFindsAsync(FollowEntity follow, Dictionary<long, FollowFindEntity> finds, CancellationToken cancellationToken)
    {
        var waiting = finds.Values.Where(f => f.Status is FollowFindStatus.New or FollowFindStatus.Wanted).ToList();
        if (waiting.Count == 0)
        {
            return;
        }

        var mamIds = waiting.Select(f => f.MamId).ToList();
        var inLibrary = (await dbContext.Torrents.Where(t => mamIds.Contains(t.MamId)).Select(t => t.MamId).ToListAsync(cancellationToken)).ToHashSet();
        var queued = (await dbContext.SelectedTorrents.Where(s => s.RemovedAt == null && mamIds.Contains(s.MamId)).Select(s => s.MamId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var ofFollow = follow.Kind == FollowKind.Series
            ? libraryQueries.TorrentIdsInSeries(follow.NameKey)
            : libraryQueries.TorrentIdsWithPerson(PersonRole.Author, follow.NameKey);
        var titleKeys = waiting.Select(f => TitleNormalizer.Normalize(f.Title)).Where(k => k.Length > 0).Distinct().ToList();
        var sameTitle = (await dbContext.Torrents.AsNoTracking()
                .Where(t => ofFollow.Contains(t.Id) && t.ReplacedWithTorrentId == null && titleKeys.Contains(t.NormalizedTitleKey))
                .Select(t => new { t.NormalizedTitleKey, t.MediaType, t.Language })
                .ToListAsync(cancellationToken))
            .Select(t => (Key: t.NormalizedTitleKey,
                Side: Enum.TryParse<MediaType>(t.MediaType, out var mediaType) ? FilterMainTypeMapper.FromMediaType(mediaType).ToString() : null,
                t.Language))
            .ToList();

        var ownedNumbers = new Dictionary<(FilterMainType, Language?), (HashSet<int> Covered, HashSet<string> Displays)>();
        var now = _time.GetUtcNow();
        var retired = 0;
        foreach (var find in waiting)
        {
            var language = LanguageOf(find);
            var titleKey = TitleNormalizer.Normalize(find.Title);
            var owned = inLibrary.Contains(find.MamId)
                || sameTitle.Any(t => t.Key == titleKey && t.Side == find.MediaSide && (language is null || t.Language == language.ToString()));
            if (!owned && follow.Kind == FollowKind.Series && find.SeriesDisplay is { } display
                && Enum.TryParse<FilterMainType>(find.MediaSide, out var side))
            {
                if (!ownedNumbers.TryGetValue((side, language), out var numbers))
                {
                    var entries = await libraryQueries.OwnedEntriesAsync(follow.NameKey, side, language, cancellationToken);
                    numbers = (
                        SeriesGaps.Covered(entries.Where(e => e.FirstNumber is not null).Select(e => (e.FirstNumber!.Value, e.LastNumber ?? e.FirstNumber!.Value))),
                        entries.Select(e => e.Display).OfType<string>().ToHashSet(StringComparer.Ordinal));
                    ownedNumbers[(side, language)] = numbers;
                }

                var entry = SeriesParser.Parse(follow.DisplayName, display).Entries.FirstOrDefault();
                owned = IsOwnedEntry(display, entry is null ? null : (double)entry.FirstNumber, entry?.LastNumber is { } last ? (double)last : null,
                    numbers.Covered, numbers.Displays);
            }

            if (owned || queued.Contains(find.MamId))
            {
                (find.Status, find.StatusAt) = (owned ? FollowFindStatus.Owned : FollowFindStatus.Selected, now);
                retired++;
            }
        }

        if (retired > 0)
        {
            _logger.LogInformation("{Name}: {Count} found earlier are in the library or selected by now", follow.DisplayName, retired);
        }
    }

    /// <summary>
    /// One find per book and format: where a book and format has more than one, as earlier searches could list a newer
    /// upload beside an older one, the most settled stays (selected, then owned, then waiting; the first found among
    /// those) and the others go. A dismissed find is never removed: it is the reader's choice.
    /// </summary>
    private void TidyDuplicateFinds(FollowEntity follow, Dictionary<long, FollowFindEntity> finds)
    {
        static int Settled(FollowFindStatus status) => status switch
        {
            FollowFindStatus.Selected => 0,
            FollowFindStatus.Owned => 1,
            _ => 2,
        };

        foreach (var book in finds.Values.Where(f => f.Status != FollowFindStatus.Dismissed)
                     .GroupBy(f => (BookKeyOf(follow, f), f.MediaSide)).Where(g => g.Count() > 1).ToList())
        {
            foreach (var extra in book.OrderBy(f => Settled(f.Status)).ThenBy(f => f.FoundAt).ThenBy(f => f.Id).Skip(1))
            {
                finds.Remove(extra.MamId);
                dbContext.FollowFinds.Remove(extra);
            }
        }
    }

    /// <summary>
    /// One decision per book and format, in the order they are taken: a series by entry number, an author's books
    /// newest first. For each, the first grab block whose filter and cost accept a torrent takes the one in the best
    /// format (then the most seeded); a match no block pays for is kept as wanted. With <c>prefer_format</c>, a book
    /// found in both formats keeps only the preferred one.
    /// </summary>
    /// <param name="follow">The series or author.</param>
    /// <param name="candidates">Its books on MAM that the library and the selection queue don't have.</param>
    /// <param name="grabFilters">The grab blocks' filters, in order.</param>
    /// <param name="books">How many books and formats the candidates are, before the grab blocks leave some out.</param>
    private List<Decision> Decide(FollowEntity follow, List<Candidate> candidates, IReadOnlyList<TorrentFilter> grabFilters, out int books)
    {
        var decisions = new List<(string BookKey, FilterMainType Side, Decision Decision, double Order, DateTimeOffset Uploaded)>();
        var groups = candidates.GroupBy(c => (c.BookKey, c.Side)).ToList();
        books = groups.Count;
        foreach (var group in groups)
        {
            var preferred = preferenceLists.For(group.First().Metadata.MediaType);
            Candidate? winner = null;
            GrabEntry? grab = null;
            Candidate? tooExpensive = null;
            for (var i = 0; i < settings.Grab.Count && winner is null; i++)
            {
                var matching = group.Where(c => TorrentFilterMatcher.Matches(grabFilters[i], GrabMatching.ToFilterCandidate(c.Record, c.Metadata))).ToList();
                var affordable = matching.Where(c => GrabMatching.Accepts(settings.Grab[i].Cost, c.Record)).ToList();
                winner = Best(affordable, preferred);
                grab = winner is null ? null : settings.Grab[i];
                tooExpensive ??= Best(matching, preferred);
            }

            if (winner is null && tooExpensive is null)
            {
                continue;
            }

            var shown = winner ?? tooExpensive!;
            decisions.Add((group.Key.BookKey, group.Key.Side, new Decision(winner, grab, winner is null ? tooExpensive : null),
                shown.SeriesNumber ?? double.MaxValue, shown.Record.UploadedAt));
        }

        if (settings.PreferFormat is { } preferredSide)
        {
            // A book in both formats keeps one: the preferred one, unless only the other can be taken at a cost a block
            // accepts. Then the one that can be taken wins, rather than nothing being downloaded.
            foreach (var book in decisions.GroupBy(d => d.BookKey).Where(g => g.Select(d => d.Side).Distinct().Count() > 1).ToList())
            {
                var preferred = book.First(d => d.Side == preferredSide);
                var keep = preferred.Decision.Winner is not null || book.All(d => d.Decision.Winner is null)
                    ? preferred
                    : book.First(d => d.Side != preferredSide && d.Decision.Winner is not null);
                decisions.RemoveAll(d => d.BookKey == book.Key && !ReferenceEquals(d.Decision, keep.Decision));
            }
        }

        var ordered = follow.Kind == FollowKind.Series
            ? decisions.OrderBy(d => d.Order).ThenBy(d => d.BookKey, StringComparer.Ordinal)
            : decisions.OrderByDescending(d => d.Uploaded);
        return ordered.Select(d => d.Decision).ToList();
    }

    private static Candidate? Best(List<Candidate> candidates, IReadOnlyList<string> preferred) =>
        candidates
            .Where(c => FormatRanker.Rank(preferred, c.Metadata.FileTypes) != FormatRanker.NotPresent)
            .OrderBy(c => FormatRanker.Rank(preferred, c.Metadata.FileTypes))
            .ThenByDescending(c => c.Record.Seeders)
            .FirstOrDefault();
}
