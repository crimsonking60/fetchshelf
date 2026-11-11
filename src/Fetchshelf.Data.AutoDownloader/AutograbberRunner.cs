using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one autograb run did.</summary>
/// <param name="ResultCount">Search results read from MAM.</param>
/// <param name="Selected">Torrents selected; in a dry run, those that would have been.</param>
/// <param name="DryRun">Whether the run was a dry run, which selects and writes nothing.</param>
/// <param name="Note">
/// Why the run stopped short, such as reaching this run's limit of selections; null if it didn't. When the limit was 0
/// from the start, nothing was searched.
/// </param>
/// <param name="CheckedForRemoval">Stored torrents `mark_removed` asked MAM about.</param>
/// <param name="MarkedRemoved">Of those, the ones MAM no longer has, marked (or in a dry run, that would have been).</param>
public sealed record AutograbRunSummary(
    int ResultCount, int Selected, bool DryRun, string? Note = null, int CheckedForRemoval = 0, int MarkedRemoved = 0)
{
    /// <summary>The selections now waiting to be downloaded: none in a dry run.</summary>
    public int SelectedForDownload => DryRun ? 0 : Selected;
}

/// <summary>Runs one `[[autograb]]` entry's periodic search-and-select cycle.</summary>
public sealed class AutograbberRunner(
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    FetchshelfDbContext dbContext,
    ILogger<AutograbberRunner>? logger = null,
    INotificationSink? notifications = null)
{
    private readonly ILogger<AutograbberRunner> _logger = logger ?? NullLogger<AutograbberRunner>.Instance;
    private readonly EventStore _eventStore = new(dbContext, notifications);

    /// <summary>
    /// Searches, and selects what passes the filter. A dry run selects nothing; what it would have selected is logged
    /// and counted in the summary.
    /// </summary>
    public async Task<AutograbRunSummary> RunAsync(
        AutograbEntry entry,
        int globalUnsatBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken = default)
    {
        var name = entry.Name ?? entry.Type?.Kind ?? "autograb";
        using var _ = await accessGate.AcquireAsync($"Autograb {name}", cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var unsatBuffer = entry.UnsatBuffer ?? globalUnsatBuffer;

        var currentActive = await SelectionQueue.ActiveForAutograbAsync(dbContext, entry, cancellationToken);

        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, entry.MaxActiveDownloads, currentActive);
        var budgetExplanation = SelectionBudget.Explain(account.UnsatLimit, account.UnsatCount, unsatBuffer, entry.MaxActiveDownloads, currentActive);

        var isMetadataOnlyCost = entry.Cost is ConfiguredCost.MetadataOnly or ConfiguredCost.MetadataOnlyAdd;
        if (budget == 0 && !isMetadataOnlyCost)
        {
            _logger.LogInformation(
                "Autograb {Name}: nothing to do, this run's limit is 0 selections ({BudgetExplanation})", name, budgetExplanation);
            return new AutograbRunSummary(0, 0, entry.DryRun, $"this run's limit is 0 selections ({budgetExplanation})");
        }

        if (entry.DryRun)
        {
            _logger.LogInformation("Autograb {Name}: dry run, nothing will be selected or written", name);
        }

        var detailLevel = entry.DryRun ? LogLevel.Information : LogLevel.Debug;
        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var selectedCount = 0;
        var resultCount = 0;
        var budgetReachedNote = string.Empty;
        string? limitNote = null;
        var returnedMamIds = new HashSet<long>();

        for (var page = 0; page < entry.EffectiveMaxPages && budgetReachedNote.Length == 0; page++)
        {
            var query = AutograbSearchTranslator.ToSearchQuery(entry, filter, page * 100L);
            var result = await mamClient.SearchAsync(query, cancellationToken);
            if (result.Data.Count == 0)
            {
                break;
            }

            resultCount += result.Data.Count;
            returnedMamIds.UnionWith(result.Data.Select(r => r.MamId));
            foreach (var record in result.Data)
            {
                TorrentMetadata metadata;
                try
                {
                    metadata = MamTorrentMapper.Map(record);
                }
                catch (UnrecognizedReferenceDataException ex)
                {
                    _logger.Log(detailLevel, "Autograb {Name}: MAM torrent {MamId} could not be read ({Reason}), skipped", name, record.MamId, ex.Message);
                    continue;
                }

                var candidate = new TorrentFilterCandidate
                {
                    Metadata = metadata,
                    UploaderName = record.UploaderName,
                    Seeders = record.Seeders,
                    Leechers = record.Leechers,
                    TimesCompleted = record.TimesCompleted,
                };

                if (!TorrentFilterMatcher.Matches(filter, candidate))
                {
                    _logger.LogDebug("Autograb {Name}: MAM torrent {MamId} \"{Title}\" does not pass the filter", name, record.MamId, metadata.Title);
                    continue;
                }

                var outcome = await selectionRunner.ProcessCandidateAsync(
                    record, metadata, ignoreTorrents, entry.Cost, entry.Category, entry.Name,
                    dryRun: entry.DryRun, cancellationToken: cancellationToken);
                _logger.Log(
                    detailLevel, "Autograb {Name}: MAM torrent {MamId} \"{Title}\": {Outcome}",
                    name, record.MamId, metadata.Title, outcome.Describe(entry.DryRun));

                if (outcome == SelectionOutcome.Selected)
                {
                    selectedCount++;
                    if (!isMetadataOnlyCost && selectedCount >= budget)
                    {
                        limitNote = $"stopped at this run's limit of {budget} selections ({budgetExplanation})";
                        budgetReachedNote = $"; {limitNote}";
                        break;
                    }
                }
            }

            if (result.Data.Count < query.PerPage)
            {
                break;
            }
        }

        var (checkedForRemoval, markedRemoved) = (0, 0);
        if (entry.MarkRemoved && returnedMamIds.Count > 0 && entry.Type?.Kind != AutograbType.Bookmarks.Kind)
        {
            (checkedForRemoval, markedRemoved) = await MarkRemovedAsync(name, entry.DryRun, returnedMamIds, cancellationToken);
        }

        _logger.LogInformation(
            "Autograb {Name}: {ResultCount} search results, {SelectedCount} {Verb}{BudgetNote}",
            name, resultCount, selectedCount, entry.DryRun ? "would have been selected (dry run)" : "selected", budgetReachedNote);
        return new AutograbRunSummary(resultCount, selectedCount, entry.DryRun, limitNote, checkedForRemoval, markedRemoved);
    }

    /// <summary>The most stored torrents one run asks MAM about for `mark_removed`.</summary>
    public const int MaxRemovalChecksPerRun = 20;

    /// <summary>How long a torrent MAM still had is left alone before `mark_removed` asks about it again.</summary>
    public static readonly TimeSpan RemovalRecheckAfter = TimeSpan.FromDays(7);

    /// <summary>
    /// Asks MAM about stored torrents whose MAM id lies within the ids the search returned but which it didn't return,
    /// and marks those MAM no longer has as removed from MAM. A gap is usually a torrent that simply doesn't match the
    /// search, so each torrent is asked about at most once per <see cref="RemovalRecheckAfter"/>, and at most
    /// <see cref="MaxRemovalChecksPerRun"/> per run, those asked about longest ago first. Returns how many were asked
    /// about, and how many of those MAM no longer has.
    /// </summary>
    private async Task<(int Checked, int Removed)> MarkRemovedAsync(string name, bool dryRun, HashSet<long> returnedMamIds, CancellationToken cancellationToken)
    {
        var (lowest, highest) = (returnedMamIds.Min(), returnedMamIds.Max());
        var now = DateTimeOffset.UtcNow;

        // SQLite can't compare DateTimeOffset values, so the recheck interval is applied here rather than in the query.
        var inRange = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.MamId >= lowest && t.MamId <= highest && t.RemovedFromMamAt == null)
            .Select(t => new { t.MamId, t.MamCheckedAt })
            .ToListAsync(cancellationToken);
        var toCheck = inRange
            .Where(t => !returnedMamIds.Contains(t.MamId))
            .GroupBy(t => t.MamId)
            .Select(g => (MamId: g.Key, CheckedAt: g.Min(t => t.MamCheckedAt)))
            .Where(t => t.CheckedAt is null || now - t.CheckedAt >= RemovalRecheckAfter)
            .OrderBy(t => t.CheckedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.MamId)
            .Take(MaxRemovalChecksPerRun)
            .Select(t => t.MamId)
            .ToList();
        if (toCheck.Count == 0)
        {
            return (0, 0);
        }

        var removed = 0;
        foreach (var mamId in toCheck)
        {
            var result = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId }, cancellationToken);
            var stillOnMam = result.Data.Count > 0;
            var torrents = await dbContext.Torrents.Where(t => t.MamId == mamId).ToListAsync(cancellationToken);
            if (!stillOnMam)
            {
                removed++;
                foreach (var torrent in torrents)
                {
                    _logger.LogInformation(
                        "Autograb {Name}: MAM no longer has {Book} (MAM id {MamId}); {Outcome}",
                        name, Describe(torrent), mamId, dryRun ? "would have been marked as removed from MAM (dry run)" : "marked as removed from MAM");
                }
            }

            if (dryRun)
            {
                continue;
            }

            foreach (var torrent in torrents)
            {
                torrent.MamCheckedAt = now;
                if (!stillOnMam)
                {
                    torrent.RemovedFromMamAt = now;
                    await _eventStore.AppendAsync(
                        new EventEntity { OccurredAt = now, TorrentId = torrent.Id, MamId = mamId, Kind = EventKind.RemovedFromMam },
                        cancellationToken);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Autograb {Name}: asked MAM about {Checked} stored {Torrents} missing from the results; {Removed} {Verb} removed",
            name, toCheck.Count, toCheck.Count == 1 ? "torrent" : "torrents", removed, dryRun ? "would have been marked (dry run)" : "marked");
        return (toCheck.Count, removed);
    }

    private static string Describe(TorrentEntity torrent)
    {
        var authors = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson).Authors;
        return authors.Count == 0 ? torrent.Title : $"{torrent.Title} by {string.Join(", ", authors)}";
    }
}
