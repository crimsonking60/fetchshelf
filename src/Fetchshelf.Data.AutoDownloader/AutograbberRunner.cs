using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>Runs one `[[autograb]]` entry's periodic search-and-select cycle, Fetchshelf-spec.md §7.1.</summary>
public sealed class AutograbberRunner(
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    FetchshelfDbContext dbContext)
{
    /// <summary>Returns the number of torrents selected during this run.</summary>
    public async Task<int> RunAsync(
        AutogrebEntry entry,
        int globalUnsatBuffer,
        int globalWedgeBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

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
            return 0;
        }

        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var selectedCount = 0;

        for (var page = 0; page < entry.EffectiveMaxPages; page++)
        {
            var query = AutogrebSearchTranslator.ToSearchQuery(entry, filter, page * 100L);
            var result = await mamClient.SearchAsync(query, cancellationToken);
            if (result.Data.Count == 0)
            {
                break;
            }

            foreach (var record in result.Data)
            {
                TorrentMetadata metadata;
                try
                {
                    metadata = MamTorrentMapper.Map(record);
                }
                catch (UnrecognizedReferenceDataException)
                {
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
                    continue;
                }

                var outcome = await selectionRunner.ProcessCandidateAsync(
                    record, metadata, ignoreTorrents, entry.Cost, entry.Category, entry.Name, unsatBuffer, wedgeBuffer, cancellationToken);

                if (outcome == SelectionOutcome.Selected)
                {
                    selectedCount++;
                    if (!isMetadataOnlyCost && selectedCount >= budget)
                    {
                        return selectedCount;
                    }
                }
            }

            if (result.Data.Count < query.PerPage)
            {
                break;
            }
        }

        return selectedCount;
    }
}
