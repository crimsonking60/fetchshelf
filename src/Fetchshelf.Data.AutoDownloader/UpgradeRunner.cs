using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one run of the Upgrade job did.</summary>
/// <param name="Searched">Library copies searched for a better one on MAM.</param>
/// <param name="Selected">Better copies selected; in a dry run, those that would have been.</param>
/// <param name="Failed">Copies whose search failed.</param>
/// <param name="DryRun">Whether the run was a dry run, which selects and writes nothing.</param>
/// <param name="SearchedTitles">The titles of the copies searched, in the order they were searched.</param>
/// <param name="Note">Why nothing was searched, when nothing was.</param>
public sealed record UpgradeRunSummary(
    int Searched, int Selected, int Failed, bool DryRun = false, IReadOnlyList<string>? SearchedTitles = null, string? Note = null)
{
    private const int TitlesShown = 5;

    /// <summary>The selections now waiting to be downloaded: none in a dry run.</summary>
    public int SelectedForDownload => DryRun ? 0 : Selected;

    /// <summary>The searched titles for a one-line message: the first few, then how many more.</summary>
    public string TitlesText
    {
        get
        {
            var titles = SearchedTitles ?? [];
            var shown = string.Join(", ", titles.Take(TitlesShown).Select(t => $"\"{t}\""));
            return titles.Count > TitlesShown ? $"{shown} and {titles.Count - TitlesShown} more" : shown;
        }
    }
}

/// <summary>
/// The Upgrade job: searches MAM for a better format of library copies that could be better
/// (<see cref="LibraryCopyRank.CouldBeUpgraded"/>), and selects one when a `[[upgrade.grab]]` block's cost accepts it.
/// The Clean job then retires the old copy once the new one is linked, as it does for any better copy.
/// </summary>
/// <remarks>
/// One MAM search per copy, at most <see cref="UpgradeSettings.SearchesPerRun"/> per run, and each copy at most once per
/// <see cref="UpgradeSettings.SearchAgainAfter"/> days, whatever the search found. The better copy goes into the old
/// copy's qBittorrent category when that is a library's, so it lands in the same library.
/// </remarks>
public sealed class UpgradeRunner(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    UpgradeSettings settings,
    IReadOnlyList<Library> libraries,
    FormatPreferenceLists preferenceLists,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger<UpgradeRunner>? logger = null)
{
    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger<UpgradeRunner> _logger = logger ?? NullLogger<UpgradeRunner>.Instance;

    public async Task<UpgradeRunSummary> RunAsync(int unsatBuffer, IReadOnlyList<long> ignoreTorrents, CancellationToken cancellationToken = default)
    {
        if (settings.DryRun)
        {
            _logger.LogInformation("Upgrade: dry run, nothing will be selected or written");
        }

        var (due, upgradable) = await DueAsync(cancellationToken);
        if (!settings.DryRun)
        {
            await ClearErrorsOfCopiesNoLongerSearchedAsync(upgradable, cancellationToken);
        }

        if (due.Count == 0)
        {
            var note = upgradable.Count == 0
                ? "no book in the library could be in a better format"
                : $"{upgradable.Count} {(upgradable.Count == 1 ? "book" : "books")} could be in a better format, but none is due a search: "
                    + $"each is searched again {settings.SearchAgainAfter} days after its last search";
            _logger.LogInformation("Upgrade: nothing searched, {Note}", note);
            return new UpgradeRunSummary(0, 0, 0, settings.DryRun, Note: note);
        }

        using var _ = await accessGate.AcquireAsync(JobNames.Upgrade, cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
        if (budget <= 0)
        {
            var explanation = SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
            var note = $"this run's limit is 0 selections ({explanation})";
            _logger.LogInformation("Upgrade: nothing searched, {Note}", note);
            return new UpgradeRunSummary(0, 0, 0, settings.DryRun, Note: note);
        }

        var grabFilters = settings.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var libraryCategories = libraries.Select(l => l.Category).OfType<string>().Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        var (selected, failed) = (0, 0);
        var searchedTitles = new List<string>();
        var errors = await ItemErrors.LoadAsync(dbContext, ErrorStage.Upgrade, settings.DryRun, cancellationToken);
        foreach (var copy in due)
        {
            if (selected >= budget)
            {
                _logger.LogInformation("Upgrade: stopped at this run's limit of {Budget} selections", budget);
                break;
            }

            searchedTitles.Add(copy.Title);
            var failure = await errors.RunAsync(
                copy.Id, copy.Title,
                async () =>
                {
                    selected += await ProcessCopyAsync(copy, grabFilters, libraryCategories, ignoreTorrents, cancellationToken) ? 1 : 0;
                    if (!settings.DryRun)
                    {
                        copy.UpgradeSearchedAt = _time.GetUtcNow();
                    }
                },
                markFailed: () => copy.UpgradeSearchedAt = _time.GetUtcNow(),
                cancellationToken);
            if (failure is not null)
            {
                failed++;
                _logger.LogWarning(failure, "Upgrade: searching for a better copy of \"{Title}\" failed: {Reason}", copy.Title, failure.Message);
            }
        }

        if (settings.DryRun)
        {
            dbContext.DiscardPendingChanges();
        }

        var summary = new UpgradeRunSummary(searchedTitles.Count, selected, failed, settings.DryRun, searchedTitles);
        _logger.LogInformation(
            "Upgrade: {Searched} {Books} searched on MAM ({Titles}), {Selected} better {Copies} {Verb}{Failures}",
            summary.Searched, summary.Searched == 1 ? "book" : "books", summary.TitlesText, selected, selected == 1 ? "copy" : "copies",
            settings.DryRun ? "would have been selected (dry run)" : "selected", failed == 0 ? string.Empty : $", {failed} failed");
        return summary;
    }

    /// <summary>
    /// The copies due a search: those that could be better, in the configured libraries, not searched within
    /// <see cref="UpgradeSettings.SearchAgainAfter"/> days; never-searched first, then the longest ago, up to
    /// <see cref="UpgradeSettings.SearchesPerRun"/>. Also returns the ids of every copy that could be better, due or
    /// not. Only the columns that decide this are read for the whole library; the chosen copies are then loaded whole.
    /// </summary>
    private async Task<(List<TorrentEntity> Due, HashSet<string> Upgradable)> DueAsync(CancellationToken cancellationToken)
    {
        var query = dbContext.Torrents.AsNoTracking().Where(t => t.LibraryPath != null && t.ReplacedWithTorrentId == null
            && (t.ChosenAudioExtension != null || t.ChosenEbookExtension != null));
        if (settings.Libraries.Count > 0)
        {
            query = query.Where(t => t.LinkerName != null && settings.Libraries.Contains(t.LinkerName));
        }

        var rows = await query
            .Select(t => new TorrentEntity
            {
                Id = t.Id,
                MamId = t.MamId,
                Title = string.Empty,
                NormalizedTitleKey = string.Empty,
                // Only needed for the media type of a copy whose column is empty, which happens when its metadata can't be read.
                MetadataJson = t.MediaType == null ? t.MetadataJson : string.Empty,
                MediaType = t.MediaType,
                CreatedAt = t.CreatedAt,
                LibraryPath = t.LibraryPath,
                LinkerName = t.LinkerName,
                ChosenAudioExtension = t.ChosenAudioExtension,
                ChosenEbookExtension = t.ChosenEbookExtension,
                UpgradeSearchedAt = t.UpgradeSearchedAt,
            })
            .ToListAsync(cancellationToken);
        var upgradable = rows.Where(t => CouldBeUpgraded(t)).ToList();

        // SQLite can't compare DateTimeOffset values, so the dates are compared here.
        var dueBefore = _time.GetUtcNow() - TimeSpan.FromDays(settings.SearchAgainAfter);
        var dueIds = upgradable
            .Where(t => t.UpgradeSearchedAt is null || t.UpgradeSearchedAt <= dueBefore)
            .OrderBy(t => t.UpgradeSearchedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.CreatedAt)
            .Take(settings.SearchesPerRun)
            .Select(t => t.Id)
            .ToList();
        var due = (await dbContext.Torrents.Where(t => dueIds.Contains(t.Id)).ToListAsync(cancellationToken))
            .OrderBy(t => dueIds.IndexOf(t.Id))
            .ToList();
        return (due, upgradable.Select(t => t.Id).ToHashSet(StringComparer.Ordinal));
    }

    private bool CouldBeUpgraded(TorrentEntity torrent)
    {
        try
        {
            return LibraryCopyRank.CouldBeUpgraded(torrent, preferenceLists);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return false; // unreadable metadata and no media type: nothing to search for
        }
    }

    /// <summary>
    /// An error of a copy that is no longer searched (replaced, unlinked, or in the best format by now) would never be
    /// cleared by a search, so it is cleared here.
    /// </summary>
    private async Task ClearErrorsOfCopiesNoLongerSearchedAsync(HashSet<string> upgradable, CancellationToken cancellationToken)
    {
        var stale = (await ErrorLog.KeysAsync(dbContext, ErrorStage.Upgrade, cancellationToken)).Where(key => !upgradable.Contains(key)).ToList();
        if (stale.Count > 0)
        {
            await dbContext.ErroredTorrents.Where(e => e.Stage == ErrorStage.Upgrade && stale.Contains(e.Key)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <returns>Whether a better copy was selected.</returns>
    private async Task<bool> ProcessCopyAsync(
        TorrentEntity copy, IReadOnlyList<TorrentFilter> grabFilters, HashSet<string> libraryCategories, IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(copy.MetadataJson);
        var copyRank = LibraryCopyRank.Of(copy, preferenceLists);

        var query = ToSearchQuery(metadata);
        var result = await mamClient.SearchAsync(query, cancellationToken);
        await _delay(PolitenessDelay, cancellationToken);

        // The same book in a better format, ranked with the list the copy's own format was chosen from. Torrents
        // ignore_torrents names, and those Fetchshelf already has or has queued, can't be selected, so they are left out.
        var preferred = LibraryCopyRank.PreferenceList(copy, preferenceLists)!;
        var mamIds = result.Data.Select(r => r.MamId).ToList();
        var known = (await dbContext.Torrents.Where(t => mamIds.Contains(t.MamId)).Select(t => t.MamId).ToListAsync(cancellationToken))
            .Concat(await dbContext.SelectedTorrents.Where(s => s.RemovedAt == null && mamIds.Contains(s.MamId)).Select(s => s.MamId).ToListAsync(cancellationToken))
            .Concat(ignoreTorrents)
            .ToHashSet();
        var (unknown, sameBook) = (0, 0);
        var better = new List<(MamTorrentRecord Record, TorrentMetadata Metadata, int Rank)>();
        foreach (var record in result.Data)
        {
            var candidate = GrabMatching.TryMap(record);
            if (known.Contains(record.MamId))
            {
                Explain(copy, record, candidate, "already known to Fetchshelf, queued or in ignore_torrents");
                continue;
            }

            unknown++;
            // The same book as selection judges it: the same title key, then the same-book test, which leaves titles out.
            if (candidate is null || TitleNormalizer.Normalize(candidate.Title) != copy.NormalizedTitleKey || !SameBookComparer.AreSameBook(metadata, candidate))
            {
                Explain(copy, record, candidate, candidate is null ? "its details can't be read" : "not the same book");
                continue;
            }

            sameBook++;
            var rank = FormatRanker.Rank(preferred, candidate.FileTypes);
            if (rank >= copyRank)
            {
                Explain(copy, record, candidate, "not in a better format");
                continue;
            }

            better.Add((record, candidate, rank));
        }

        better = [.. better.OrderBy(c => c.Rank).ThenByDescending(c => c.Record.Seeders)];
        var counts = $"searched for {query.Text} in {metadata.Language}, linked as {CopyFormat(copy)}: {Count(result.Data.Count, "result", "results")}, "
            + $"{unknown} not known yet, {sameBook} the same book, {better.Count} in a better format";
        if (better.Count == 0)
        {
            _logger.LogInformation("Upgrade \"{Title}\": {Counts}; no better copy", copy.Title, counts);
            return false;
        }

        // Each block in turn, with the copies its filter and cost accept, best first: one that selection turns down
        // (such as a better copy selected meanwhile) leaves the next one to try.
        var tries = settings.Grab
            .SelectMany((grab, i) => better
                .Where(c => TorrentFilterMatcher.Matches(grabFilters[i], GrabMatching.ToFilterCandidate(c.Record, c.Metadata)) && GrabMatching.Accepts(grab.Cost, c.Record))
                .Select(c => (c.Record, c.Metadata, Grab: grab)))
            .DistinctBy(c => c.Record.MamId)
            .ToList();
        if (tries.Count == 0)
        {
            _logger.LogInformation(
                "Upgrade \"{Title}\": {Counts}; a better copy is on MAM (torrent {MamId}), but no grab block's filter and cost accept it",
                copy.Title, counts, better[0].Record.MamId);
            return false;
        }

        var category = copy.QBittorrentCategory is { } own && libraryCategories.Contains(own) ? own : null;
        foreach (var (record, candidate, grab) in tries)
        {
            var outcome = await selectionRunner.ProcessCandidateAsync(
                record, candidate, ignoreTorrents, grab.Cost, autograbCategory: category, grabberName: JobNames.Upgrade,
                dryRun: settings.DryRun, formatList: preferred, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Upgrade \"{Title}\": {Counts}; MAM torrent {MamId} ({Formats}): {Outcome}",
                copy.Title, counts, record.MamId, string.Join(", ", candidate.FileTypes), outcome.Describe(settings.DryRun));
            if (outcome.IsSelected())
            {
                return true;
            }

            if (outcome is SelectionOutcome.DuplicateOfLibrary or SelectionOutcome.DuplicateOfSelected)
            {
                // An equal or better copy is in the library or the queue already: the rest are no better than this one.
                return false;
            }
        }

        return false;
    }

    /// <summary>Why a search result is not a better copy, at debug level: one line per result.</summary>
    private void Explain(TorrentEntity copy, MamTorrentRecord record, TorrentMetadata? candidate, string why) =>
        _logger.LogDebug(
            "Upgrade \"{Title}\": MAM torrent {MamId} \"{ResultTitle}\" by {Authors} ({Formats}): {Why}",
            copy.Title, record.MamId, candidate?.Title ?? record.Title, string.Join(", ", candidate?.Authors ?? [.. record.Authors.Values]),
            candidate is null ? "?" : string.Join(", ", candidate.FileTypes), why);

    private static string CopyFormat(TorrentEntity copy) =>
        string.Join(" and ", new[] { copy.ChosenAudioExtension, copy.ChosenEbookExtension }.OfType<string>());

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    /// <summary>
    /// The title and authors as a Hardcover list searches them (<see cref="ListMamQueryBuilder.Build"/>), in the copy's
    /// language and, for audiobooks and ebooks, its main category.
    /// </summary>
    private static MamSearchQuery ToSearchQuery(TorrentMetadata metadata) => new()
    {
        Text = ListMamQueryBuilder.Build(metadata.Title, metadata.Authors),
        SearchFields = [MamSearchField.Title, MamSearchField.Author],
        Target = MamSearchTarget.New,
        LanguageIds = [(int)metadata.Language],
        // Audiobooks and ebooks are searched in their own main category; music and radio, rare here, anywhere.
        MainCategoryIds = FilterMainTypeMapper.FromMediaType(metadata.MediaType) switch
        {
            FilterMainType.Audio => AutograbSearchTranslator.ToMainCategoryIds(new CategoryFilter { Ebook = MainTypeCategoryFilter.None }),
            FilterMainType.Ebook => AutograbSearchTranslator.ToMainCategoryIds(new CategoryFilter { Audio = MainTypeCategoryFilter.None }),
            _ => [],
        },
        PerPage = 100,
        ReturnDownloadLink = true,
    };
}
