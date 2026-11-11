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
public sealed record UpgradeRunSummary(int Searched, int Selected, int Failed, bool DryRun = false)
{
    /// <summary>The selections now waiting to be downloaded: none in a dry run.</summary>
    public int SelectedForDownload => DryRun ? 0 : Selected;
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

        var due = await DueAsync(cancellationToken);
        if (due.Count == 0)
        {
            _logger.LogInformation("Upgrade: no book in the library is due a search for a better copy");
            return new UpgradeRunSummary(0, 0, 0, settings.DryRun);
        }

        using var _ = await accessGate.AcquireAsync(JobNames.Upgrade, cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
        if (budget <= 0)
        {
            var explanation = SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
            _logger.LogInformation("Upgrade: nothing searched, this run's limit is 0 selections ({BudgetExplanation})", explanation);
            return new UpgradeRunSummary(0, 0, 0, settings.DryRun);
        }

        var grabFilters = settings.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var libraryCategories = libraries.Select(l => l.Category).OfType<string>().Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        var (searched, selected, failed) = (0, 0, 0);
        var erroredKeys = settings.DryRun ? [] : await ErrorLog.KeysAsync(dbContext, ErrorStage.Upgrade, cancellationToken);
        foreach (var copy in due)
        {
            if (selected >= budget)
            {
                _logger.LogInformation("Upgrade: stopped at this run's limit of {Budget} selections", budget);
                break;
            }

            try
            {
                searched++;
                selected += await ProcessCopyAsync(copy, grabFilters, libraryCategories, ignoreTorrents, cancellationToken) ? 1 : 0;
                if (!settings.DryRun)
                {
                    copy.UpgradeSearchedAt = _time.GetUtcNow();
                    if (erroredKeys.Remove(copy.Id))
                    {
                        await ErrorLog.ClearAsync(dbContext, ErrorStage.Upgrade, copy.Id, cancellationToken);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is MamAccountBlockedException or MamRateLimitedException or MamForbiddenException)
            {
                // Every later search would fail the same way: the run fails, and the copy has no error of its own.
                dbContext.DiscardPendingChanges();
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex, "Upgrade: searching for a better copy of \"{Title}\" failed: {Reason}", copy.Title, ex.Message);
                dbContext.DiscardPendingChanges();
                if (!settings.DryRun)
                {
                    // Counted as searched, so a copy that keeps failing waits its turn instead of taking a search from the others.
                    copy.UpgradeSearchedAt = _time.GetUtcNow();
                    await ErrorLog.RecordAsync(dbContext, ErrorStage.Upgrade, copy.Id, copy.Title, ex.Message, cancellationToken: cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    erroredKeys.Add(copy.Id);
                }
            }
        }

        if (settings.DryRun)
        {
            dbContext.DiscardPendingChanges();
        }

        _logger.LogInformation(
            "Upgrade: {Searched} {Books} searched on MAM, {Selected} better {Copies} {Verb}{Failures}",
            searched, searched == 1 ? "book" : "books", selected, selected == 1 ? "copy" : "copies",
            settings.DryRun ? "would have been selected (dry run)" : "selected", failed == 0 ? string.Empty : $", {failed} failed");
        return new UpgradeRunSummary(searched, selected, failed, settings.DryRun);
    }

    /// <summary>
    /// The copies due a search: those that could be better, in the configured libraries, not searched within
    /// <see cref="UpgradeSettings.SearchAgainAfter"/> days; never-searched first, then the longest ago, up to
    /// <see cref="UpgradeSettings.SearchesPerRun"/>.
    /// </summary>
    private async Task<List<TorrentEntity>> DueAsync(CancellationToken cancellationToken)
    {
        var query = dbContext.Torrents.Where(t => t.LibraryPath != null && t.ReplacedWithTorrentId == null
            && (t.ChosenAudioExtension != null || t.ChosenEbookExtension != null));
        if (settings.Libraries.Count > 0)
        {
            query = query.Where(t => t.LinkerName != null && settings.Libraries.Contains(t.LinkerName));
        }

        // SQLite can't compare DateTimeOffset values, so the dates are compared here, on the linked copies only.
        var dueBefore = _time.GetUtcNow() - TimeSpan.FromDays(settings.SearchAgainAfter);
        return (await query.ToListAsync(cancellationToken))
            .Where(t => (t.UpgradeSearchedAt is null || t.UpgradeSearchedAt <= dueBefore) && LibraryCopyRank.CouldBeUpgraded(t, preferenceLists))
            .OrderBy(t => t.UpgradeSearchedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.CreatedAt)
            .Take(settings.SearchesPerRun)
            .ToList();
    }

    /// <returns>Whether a better copy was selected.</returns>
    private async Task<bool> ProcessCopyAsync(
        TorrentEntity copy, IReadOnlyList<TorrentFilter> grabFilters, HashSet<string> libraryCategories, IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(copy.MetadataJson);
        var copyRank = LibraryCopyRank.Of(copy, preferenceLists);
        var side = copy.ChosenAudioExtension is not null ? FilterMainType.Audio : FilterMainType.Ebook;

        var result = await mamClient.SearchAsync(ToSearchQuery(metadata), cancellationToken);
        await _delay(PolitenessDelay, cancellationToken);

        // The same book in a better format: ranked with the list the copy's own format is ranked with.
        var preferred = side == FilterMainType.Audio ? preferenceLists.AudioSideFor(metadata.MediaType) : preferenceLists.EbookTypes;
        var better = result.Data
            .Where(record => record.MamId != copy.MamId)
            .Select(record => (Record: record, Metadata: TryMap(record)))
            // The same book as selection judges it: the same title key, then the same-book test, which leaves titles out.
            .Where(c => c.Metadata is not null && TitleNormalizer.Normalize(c.Metadata.Title) == copy.NormalizedTitleKey
                && SameBookComparer.AreSameBook(metadata, c.Metadata))
            .Select(c => (c.Record, Metadata: c.Metadata!, Rank: FormatRanker.Rank(preferred, c.Metadata!.FileTypes)))
            .Where(c => c.Rank < copyRank)
            .OrderBy(c => c.Rank)
            .ThenByDescending(c => c.Record.Seeders)
            .ToList();
        if (better.Count == 0)
        {
            Log(copy, "no better copy on MAM");
            return false;
        }

        // The first block whose filter and cost accept one takes the best of those it accepts.
        for (var i = 0; i < settings.Grab.Count; i++)
        {
            var grab = settings.Grab[i];
            var taken = better.FirstOrDefault(c => TorrentFilterMatcher.Matches(grabFilters[i], ToFilterCandidate(c.Record, c.Metadata)) && Accepts(grab.Cost, c.Record));
            if (taken.Record is null)
            {
                continue;
            }

            var category = copy.QBittorrentCategory is { } own && libraryCategories.Contains(own) ? own : null;
            var outcome = await selectionRunner.ProcessCandidateAsync(
                taken.Record, taken.Metadata, ignoreTorrents, grab.Cost, autograbCategory: category, grabberName: JobNames.Upgrade,
                dryRun: settings.DryRun, cancellationToken: cancellationToken);
            Log(copy, $"MAM torrent {taken.Record.MamId} ({string.Join(", ", taken.Metadata.FileTypes)}): {outcome.Describe(settings.DryRun)}");
            return outcome.IsSelected();
        }

        Log(copy, $"a better copy is on MAM (torrent {better[0].Record.MamId}), but no grab block's cost accepts it");
        return false;
    }

    private void Log(TorrentEntity copy, string what) =>
        _logger.Log(settings.DryRun ? LogLevel.Information : LogLevel.Debug, "Upgrade \"{Title}\": {What}", copy.Title, what);

    /// <summary>The title with the first author, in the copy's language and media side, searched in titles and authors.</summary>
    private static MamSearchQuery ToSearchQuery(TorrentMetadata metadata) => new()
    {
        Text = ListMamQueryBuilder.PlainWords(string.Join(' ', new[] { metadata.Title, metadata.Authors.FirstOrDefault() ?? string.Empty })),
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

    private static TorrentMetadata? TryMap(MamTorrentRecord record)
    {
        try
        {
            return MamTorrentMapper.Map(record);
        }
        catch (UnrecognizedReferenceDataException)
        {
            return null;
        }
    }

    /// <summary>Whether a grab block with <paramref name="cost"/> may take <paramref name="record"/>: the rule selection applies.</summary>
    private static bool Accepts(ConfiguredCost cost, MamTorrentRecord record) =>
        CostPolicy.Derive(record.IsVip, record.IsPersonalFreeleech, record.IsFree, cost) is not null;

    private static TorrentFilterCandidate ToFilterCandidate(MamTorrentRecord record, TorrentMetadata metadata) => new()
    {
        Metadata = metadata,
        UploaderName = record.UploaderName,
        Seeders = record.Seeders,
        Leechers = record.Leechers,
        TimesCompleted = record.TimesCompleted,
    };
}
