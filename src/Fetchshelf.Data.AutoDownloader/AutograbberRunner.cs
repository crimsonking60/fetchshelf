using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>Runs one `[[autograb]]` entry's periodic search-and-select cycle, Fetchshelf-spec.md §7.1.</summary>
public sealed class AutograbberRunner(
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    FetchshelfDbContext dbContext,
    ILogger<AutograbberRunner>? logger = null)
{
    private readonly ILogger<AutograbberRunner> _logger = logger ?? NullLogger<AutograbberRunner>.Instance;

    /// <summary>
    /// Returns the number of torrents selected during this run. A dry run selects nothing and returns 0; what it
    /// would have selected is only logged.
    /// </summary>
    public async Task<int> RunAsync(
        AutogrebEntry entry,
        int globalUnsatBuffer,
        int globalWedgeBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var name = entry.Name ?? entry.Type?.Kind ?? "autograb";
        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var unsatBuffer = entry.UnsatBuffer ?? globalUnsatBuffer;
        var wedgeBuffer = entry.WedgeBuffer ?? globalWedgeBuffer;

        var currentActive = entry.MaxActiveDownloads is null
            ? 0
            : await dbContext.SelectedTorrents.CountAsync(
                s => s.GrabberName == entry.Name && s.RemovedAt == null && s.StartedAt == null, cancellationToken);

        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, entry.MaxActiveDownloads, currentActive);

        var isMetadataOnlyCost = entry.Cost is ConfiguredCost.MetadataOnly or ConfiguredCost.MetadataOnlyAdd;
        if (budget == 0 && !isMetadataOnlyCost)
        {
            _logger.LogInformation(
                "Autograb {Name}: nothing to do, no unsatisfied-snatch slots free beyond the buffer of {UnsatBuffer} or the active-download limit is reached",
                name, unsatBuffer);
            return 0;
        }

        if (entry.DryRun)
        {
            _logger.LogInformation("Autograb {Name}: dry run, nothing will be selected or written", name);
        }

        var detailLevel = entry.DryRun ? LogLevel.Information : LogLevel.Debug;
        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var selectedCount = 0;
        var resultCount = 0;

        for (var page = 0; page < entry.EffectiveMaxPages; page++)
        {
            var query = AutogrebSearchTranslator.ToSearchQuery(entry, filter, page * 100L);
            var result = await mamClient.SearchAsync(query, cancellationToken);
            if (result.Data.Count == 0)
            {
                break;
            }

            resultCount += result.Data.Count;
            foreach (var record in result.Data)
            {
                TorrentMetadata metadata;
                try
                {
                    metadata = MamTorrentMapper.Map(record);
                }
                catch (UnrecognizedReferenceDataException ex)
                {
                    _logger.Log(detailLevel, "Autograb {Name}: MaM torrent {MamId} could not be read ({Reason}), skipped", name, record.MamId, ex.Message);
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
                    _logger.LogDebug("Autograb {Name}: MaM torrent {MamId} \"{Title}\" does not pass the filter", name, record.MamId, metadata.Title);
                    continue;
                }

                var outcome = await selectionRunner.ProcessCandidateAsync(
                    record, metadata, ignoreTorrents, entry.Cost, entry.Category, entry.Name, unsatBuffer, wedgeBuffer,
                    dryRun: entry.DryRun, cancellationToken: cancellationToken);
                _logger.Log(
                    detailLevel, "Autograb {Name}: MaM torrent {MamId} \"{Title}\": {Outcome}",
                    name, record.MamId, metadata.Title, outcome.Describe(entry.DryRun));

                if (outcome == SelectionOutcome.Selected)
                {
                    selectedCount++;
                    if (!isMetadataOnlyCost && selectedCount >= budget)
                    {
                        return Finish(name, entry.DryRun, resultCount, selectedCount, budgetReached: true);
                    }
                }
            }

            if (result.Data.Count < query.PerPage)
            {
                break;
            }
        }

        return Finish(name, entry.DryRun, resultCount, selectedCount, budgetReached: false);
    }

    private int Finish(string name, bool dryRun, int resultCount, int selectedCount, bool budgetReached)
    {
        _logger.LogInformation(
            "Autograb {Name}: {ResultCount} search results, {SelectedCount} {Verb}{BudgetNote}",
            name, resultCount, selectedCount, dryRun ? "would have been selected (dry run)" : "selected",
            budgetReached ? "; stopped because the run's limit was reached" : string.Empty);
        return dryRun ? 0 : selectedCount;
    }
}
