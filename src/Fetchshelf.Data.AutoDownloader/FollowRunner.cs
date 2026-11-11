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
public sealed record FollowRunSummary(int Searched, int Selected, int NewFinds, int Failed, bool DryRun = false)
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
/// per <see cref="FollowSettings.SearchAgainAfter"/> hours. MAM's search matches words, so a result counts only when its
/// series (or author) has the followed name's key, the same key the library's series and people rows have. What the
/// library already has, or the selection queue holds, is never a find.
/// </remarks>
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
    ILogger<FollowRunner>? logger = null)
{
    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger<FollowRunner> _logger = logger ?? NullLogger<FollowRunner>.Instance;

    /// <summary>The grabber name the job's selections carry, so the Selected page says which follow chose them.</summary>
    public static string GrabberName(FollowEntity follow) => $"Follow: {follow.DisplayName}";

    public async Task<FollowRunSummary> RunAsync(int unsatBuffer, IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var searchAgainAfter = TimeSpan.FromHours(settings.SearchAgainAfter);

        // A few hundred rows at most; DateTimeOffset comparisons don't translate to SQLite anyway.
        var due = (await dbContext.Follows.ToListAsync(cancellationToken))
            .Where(f => f.Mode != FollowMode.Ignore && (f.LastSearchedAt is null || f.LastSearchedAt <= now - searchAgainAfter))
            .OrderBy(f => f.LastSearchedAt ?? DateTimeOffset.MinValue)
            .ThenBy(f => f.Id)
            .Take(settings.SearchesPerRun)
            .ToList();
        if (settings.DryRun)
        {
            _logger.LogInformation("Follow: dry run, nothing will be selected or written");
        }

        if (due.Count == 0)
        {
            _logger.LogInformation("Follow: no followed series or author is due for a search");
            return new FollowRunSummary(0, 0, 0, 0, settings.DryRun);
        }

        using var _ = await accessGate.AcquireAsync(JobNames.Follow, cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
        if (budget <= 0 && due.All(f => f.Mode == FollowMode.Download))
        {
            var explanation = SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
            _logger.LogInformation("Follow: nothing searched, this run's limit is 0 selections ({BudgetExplanation})", explanation);
            return new FollowRunSummary(0, 0, 0, 0, settings.DryRun);
        }

        var grabFilters = settings.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var run = new RunState((int)Math.Clamp(budget, 0, int.MaxValue));
        var erroredKeys = settings.DryRun ? [] : await ErrorLog.KeysAsync(dbContext, ErrorStage.Follow, cancellationToken);
        foreach (var follow in due)
        {
            if (follow.Mode == FollowMode.Download && run.BudgetLeft <= 0)
            {
                // Watched follows select nothing, so they are still searched.
                continue;
            }

            var key = ErrorLog.FollowKey(follow.Id);
            try
            {
                await ProcessFollowAsync(follow, grabFilters, ignoreTorrents, run, cancellationToken);
                if (!settings.DryRun)
                {
                    if (erroredKeys.Remove(key))
                    {
                        await ErrorLog.ClearAsync(dbContext, ErrorStage.Follow, key, cancellationToken);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is MamAccountBlockedException or MamRateLimitedException or MamForbiddenException)
            {
                // Every later search would fail the same way: the run fails, and the follow has no error of its own.
                dbContext.DiscardPendingChanges();
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                run.Failed++;
                _logger.LogWarning(ex, "Follow: searching for {Name} failed: {Reason}", follow.DisplayName, ex.Message);
                // What the failed follow changed must not be saved along with the next one.
                dbContext.DiscardPendingChanges();
                if (!settings.DryRun)
                {
                    // Counted as searched, so a follow that keeps failing waits its turn instead of taking a search
                    // from the others on every run. The Errors page shows it until a search succeeds.
                    follow.LastSearchedAt = _time.GetUtcNow();
                    await ErrorLog.RecordAsync(dbContext, ErrorStage.Follow, key, follow.DisplayName, ex.Message, cancellationToken: cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    erroredKeys.Add(key);
                }
            }
        }

        if (settings.DryRun)
        {
            dbContext.DiscardPendingChanges();
        }

        _logger.LogInformation(
            "Follow: {Searched} followed searched on MAM, {Selected} {Verb}, {New} new to look at{Failures}",
            run.Searched, run.Selected, settings.DryRun ? "would have been selected (dry run)" : "selected", run.NewFinds,
            run.Failed == 0 ? string.Empty : $", {run.Failed} failed");
        return new FollowRunSummary(run.Searched, run.Selected, run.NewFinds, run.Failed, settings.DryRun);
    }

    private sealed class RunState(int budget)
    {
        public int BudgetLeft { get; set; } = budget;

        public int Searched { get; set; }

        public int Selected { get; set; }

        public int NewFinds { get; set; }

        public int Failed { get; set; }
    }

    /// <summary>A search result that is a book of the followed series or author.</summary>
    private sealed record Candidate(
        MamTorrentRecord Record, TorrentMetadata Metadata, FilterMainType Side, string BookKey, string? SeriesDisplay, double? SeriesNumber,
        double? SeriesLastNumber = null);

    /// <summary>What to do about one book in one format: take the winner with its block's cost, or note the match no block pays for.</summary>
    private sealed record Decision(Candidate? Winner, GrabEntry? Grab, Candidate? TooExpensive)
    {
        public Candidate Shown => Winner ?? TooExpensive!;
    }

    private async Task ProcessFollowAsync(
        FollowEntity follow, IReadOnlyList<TorrentFilter> grabFilters, IReadOnlyList<long> ignoreTorrents, RunState run,
        CancellationToken cancellationToken)
    {
        var result = await mamClient.SearchAsync(ToSearchQuery(follow, grabFilters), cancellationToken);
        await _delay(PolitenessDelay, cancellationToken);
        run.Searched++;

        var candidates = result.Data.Select(record => ToCandidate(follow, record)).OfType<Candidate>().ToList();
        var finds = await dbContext.FollowFinds.Where(f => f.FollowId == follow.Id).ToDictionaryAsync(f => f.MamId, cancellationToken);
        candidates = await WithoutOwnedAsync(follow, candidates, finds, cancellationToken);

        var decisions = Decide(follow, candidates, grabFilters);
        var now = _time.GetUtcNow();
        var foundSomething = false;
        var stoppedByBudget = false;
        foreach (var decision in decisions)
        {
            var shown = decision.Shown;
            if (finds.TryGetValue(shown.Record.MamId, out var known)
                && (known.Status is FollowFindStatus.Dismissed or FollowFindStatus.Selected
                    || (known.Status is FollowFindStatus.New && follow.Mode == FollowMode.Watch)
                    || (known.Status is FollowFindStatus.Wanted && decision.Winner is null)))
            {
                continue;
            }

            if (decision.Winner is null)
            {
                Record(follow, shown, FollowFindStatus.Wanted, finds, now);
                Log(follow, shown, "matches, but no grab block's cost accepts it");
                foundSomething = true;
                continue;
            }

            if (follow.Mode == FollowMode.Watch)
            {
                Record(follow, shown, FollowFindStatus.New, finds, now);
                run.NewFinds++;
                foundSomething = true;
                Log(follow, shown, "listed on the Following page");
                continue;
            }

            if (run.BudgetLeft <= 0)
            {
                stoppedByBudget = true;
                _logger.LogInformation("Follow: stopped at this run's limit of selections; {Name} is searched again next run", follow.DisplayName);
                break;
            }

            var outcome = await selectionRunner.ProcessCandidateAsync(
                shown.Record, shown.Metadata, ignoreTorrents, decision.Grab!.Cost, autograbCategory: settings.Category,
                grabberName: GrabberName(follow), dryRun: settings.DryRun, cancellationToken: cancellationToken);
            Log(follow, shown, outcome.Describe(settings.DryRun));
            switch (outcome)
            {
                case SelectionOutcome.Selected:
                    Record(follow, shown, FollowFindStatus.Selected, finds, now);
                    run.Selected++;
                    run.BudgetLeft--;
                    foundSomething = true;
                    break;
                case SelectionOutcome.CostNotFree:
                    Record(follow, shown, FollowFindStatus.Wanted, finds, now);
                    foundSomething = true;
                    break;
            }
        }

        if (!stoppedByBudget)
        {
            follow.LastSearchedAt = now;
        }

        if (foundSomething)
        {
            follow.LastFoundAt = now;
        }
    }

    private void Log(FollowEntity follow, Candidate candidate, string what) =>
        _logger.Log(
            settings.DryRun ? LogLevel.Information : LogLevel.Debug, "Follow {Name}: \"{Title}\" (MAM torrent {MamId}): {What}",
            follow.DisplayName, candidate.Metadata.Title, candidate.Record.MamId, what);

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
                Status = status,
                FoundAt = now,
                StatusAt = now,
            };
            finds[find.MamId] = find;
            dbContext.FollowFinds.Add(find);
            return;
        }

        (find.Status, find.StatusAt) = (status, now);
    }

    /// <summary>
    /// The search: the followed name in MAM's series (or author) field, newest first, so a series or author with more
    /// than one page of torrents still has its new ones seen. Limited to the languages and main categories every grab
    /// block together allows; each block's own filter is applied to the results afterwards.
    /// </summary>
    private static MamSearchQuery ToSearchQuery(FollowEntity follow, IReadOnlyList<TorrentFilter> grabFilters)
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
            PerPage = 100,
            ReturnDownloadLink = true,
        };
    }

    /// <summary>The result as a candidate when it really is a book of the followed series or author, as an audiobook or ebook.</summary>
    private static Candidate? ToCandidate(FollowEntity follow, MamTorrentRecord record)
    {
        TorrentMetadata metadata;
        try
        {
            metadata = MamTorrentMapper.Map(record);
        }
        catch (UnrecognizedReferenceDataException)
        {
            return null;
        }

        var side = FilterMainTypeMapper.FromMediaType(metadata.MediaType);
        if (side is not (FilterMainType.Audio or FilterMainType.Ebook))
        {
            return null;
        }

        var projection = TorrentProjection.From(metadata);
        if (follow.Kind == FollowKind.Author)
        {
            return projection.People.Any(p => p.Role == PersonRole.Author && p.NameKey == follow.NameKey)
                ? new Candidate(record, metadata, side, "title:" + TitleNormalizer.Normalize(metadata.Title), null, null)
                : null;
        }

        var entry = projection.Series.FirstOrDefault(s => s.NameKey == follow.NameKey);
        return entry is null
            ? null
            : new Candidate(
                record, metadata, side,
                entry.Display is null ? "title:" + TitleNormalizer.Normalize(metadata.Title) : "entry:" + entry.Display,
                entry.Display, entry.FirstNumber is { } number ? (double)number : null, entry.LastNumber is { } last ? (double)last : null);
    }

    /// <summary>
    /// Leaves out what the library or the selection queue has: the same MAM torrent, the same book (as selection judges
    /// it), or, for a series, an entry already owned in that format. A find for such a torrent is marked owned or
    /// selected, so the Following page stops offering it.
    /// </summary>
    private async Task<List<Candidate>> WithoutOwnedAsync(
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

        var ownedNumbers = new Dictionary<FilterMainType, (HashSet<int> Covered, HashSet<string> Displays)>();
        if (follow.Kind == FollowKind.Series)
        {
            foreach (var side in new[] { FilterMainType.Audio, FilterMainType.Ebook })
            {
                var owned = await libraryQueries.OwnedEntriesAsync(follow.NameKey, side, cancellationToken);
                ownedNumbers[side] = (
                    SeriesGaps.Covered(owned.Where(e => e.FirstNumber is not null).Select(e => (e.FirstNumber!.Value, e.LastNumber ?? e.FirstNumber!.Value))),
                    owned.Select(e => e.Display).OfType<string>().ToHashSet(StringComparer.Ordinal));
            }
        }

        var kept = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            var mamId = candidate.Record.MamId;
            var titleKey = TitleNormalizer.Normalize(candidate.Metadata.Title);
            var owned = inLibrary.Contains(mamId)
                || sameTitle.Any(t => t.Key == titleKey && SameBookComparer.AreSameBook(candidate.Metadata, t.Metadata))
                || (ownedNumbers.TryGetValue(candidate.Side, out var numbers) && IsOwnedEntry(candidate, numbers.Covered, numbers.Displays));
            if (owned || queued.Contains(mamId))
            {
                if (finds.TryGetValue(mamId, out var find) && find.Status is FollowFindStatus.New or FollowFindStatus.Wanted)
                {
                    (find.Status, find.StatusAt) = (owned ? FollowFindStatus.Owned : FollowFindStatus.Selected, _time.GetUtcNow());
                }

                continue;
            }

            kept.Add(candidate);
        }

        return kept;
    }

    private static bool IsOwnedEntry(Candidate candidate, HashSet<int> covered, HashSet<string> displays)
    {
        if (candidate.SeriesDisplay is null)
        {
            return false;
        }

        if (displays.Contains(candidate.SeriesDisplay))
        {
            return true;
        }

        // Owned when owned entries cover every book it holds: 2 within an owned 1-3, but not an omnibus 3-5 when only 3
        // is owned. An entry holding no whole book (1.5) is only owned under its own display.
        if (candidate.SeriesNumber is not { } first)
        {
            return false;
        }

        var books = SeriesGaps.Covered([(first, candidate.SeriesLastNumber ?? first)]);
        return books.Count > 0 && books.IsSubsetOf(covered);
    }

    /// <summary>
    /// One decision per book and format, in the order they are taken: a series by entry number, an author's books
    /// newest first. For each, the first grab block whose filter and cost accept a torrent takes the one in the best
    /// format (then the most seeded); a match no block pays for is kept as wanted. With <c>prefer_format</c>, a book
    /// found in both formats keeps only the preferred one.
    /// </summary>
    private List<Decision> Decide(FollowEntity follow, List<Candidate> candidates, IReadOnlyList<TorrentFilter> grabFilters)
    {
        var decisions = new List<(string BookKey, FilterMainType Side, Decision Decision, double Order, DateTimeOffset Uploaded)>();
        foreach (var group in candidates.GroupBy(c => (c.BookKey, c.Side)))
        {
            var preferred = preferenceLists.For(group.First().Metadata.MediaType);
            Candidate? winner = null;
            GrabEntry? grab = null;
            Candidate? tooExpensive = null;
            for (var i = 0; i < settings.Grab.Count && winner is null; i++)
            {
                var matching = group.Where(c => TorrentFilterMatcher.Matches(grabFilters[i], ToFilterCandidate(c))).ToList();
                var affordable = matching.Where(c => Accepts(settings.Grab[i].Cost, c.Record)).ToList();
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

    /// <summary>Whether a grab block with <paramref name="cost"/> may take <paramref name="record"/>: the rule selection applies.</summary>
    private static bool Accepts(ConfiguredCost cost, MamTorrentRecord record) =>
        CostPolicy.Derive(record.IsVip, record.IsPersonalFreeleech, record.IsFree, cost) is not null;

    private static TorrentFilterCandidate ToFilterCandidate(Candidate candidate) => new()
    {
        Metadata = candidate.Metadata,
        UploaderName = candidate.Record.UploaderName,
        Seeders = candidate.Record.Seeders,
        Leechers = candidate.Record.Leechers,
        TimesCompleted = candidate.Record.TimesCompleted,
    };
}
