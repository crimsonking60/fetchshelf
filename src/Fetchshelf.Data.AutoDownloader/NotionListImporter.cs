using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.Notion;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Imports a `[[notion_list]]` entry: queries the configured Notion data source, reads a MaM id from
/// each row's configured URL properties, and feeds newly-seen torrents through the existing selection
/// pipeline, Fetchshelf-spec.md §7.8 "Notion list". Stateless - no persisted `List`/`ListItem` rows, keyed
/// off the existing Torrents table to skip ids already known.
/// </summary>
public sealed class NotionListImporter(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    SelectionRunner selectionRunner,
    NotionClient notionClient,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));

    /// <summary>Returns the number of torrents selected during this run.</summary>
    public async Task<int> RunAsync(
        NotionListEntry entry,
        int globalUnsatBuffer,
        int globalWedgeBuffer,
        IReadOnlyList<long> ignoreTorrents,
        CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var unsatBuffer = entry.UnsatBuffer ?? globalUnsatBuffer;
        var wedgeBuffer = entry.WedgeBuffer ?? globalWedgeBuffer;
        var budget = SelectionBudget.Compute(account.UnsatLimit, account.UnsatCount, unsatBuffer, maxActiveDownloads: null, currentActiveForGrabber: 0);
        if (budget <= 0)
        {
            return 0;
        }

        var response = await notionClient.QueryDataSourceAsync(entry.DataSource, entry.Token, cancellationToken);
        var grabFilters = entry.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();

        var selectedCount = 0;
        foreach (var row in response.Results)
        {
            foreach (var mamId in NotionMamIdExtractor.ExtractMamIds(row, entry.MamFields))
            {
                if (selectedCount >= budget)
                {
                    return selectedCount;
                }

                var alreadyKnown = await dbContext.Torrents.AnyAsync(t => t.MamId == mamId, cancellationToken);
                if (alreadyKnown)
                {
                    continue;
                }

                selectedCount += await ProcessMamIdAsync(entry, grabFilters, mamId, ignoreTorrents, unsatBuffer, wedgeBuffer, cancellationToken);
                await _delay(PolitenessDelay, cancellationToken);
            }
        }

        return selectedCount;
    }

    private async Task<int> ProcessMamIdAsync(
        NotionListEntry entry,
        IReadOnlyList<TorrentFilter> grabFilters,
        long mamId,
        IReadOnlyList<long> ignoreTorrents,
        int unsatBuffer,
        int wedgeBuffer,
        CancellationToken cancellationToken)
    {
        var searchResult = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnDownloadLink = true }, cancellationToken);
        var record = searchResult.Data.FirstOrDefault();
        if (record is null)
        {
            return 0;
        }

        TorrentMetadata metadata;
        try
        {
            metadata = MamTorrentMapper.Map(record);
        }
        catch (UnrecognizedReferenceDataException)
        {
            return 0;
        }

        var candidate = new TorrentFilterCandidate
        {
            Metadata = metadata,
            UploaderName = record.UploaderName,
            Seeders = record.Seeders,
            Leechers = record.Leechers,
            TimesCompleted = record.TimesCompleted,
        };

        for (var i = 0; i < entry.Grab.Count; i++)
        {
            if (!TorrentFilterMatcher.Matches(grabFilters[i], candidate))
            {
                continue;
            }

            if (entry.DryRun)
            {
                return 0;
            }

            var outcome = await selectionRunner.ProcessCandidateAsync(
                record, metadata, ignoreTorrents, entry.Grab[i].Cost, autogrebCategory: null, grabberName: entry.Name,
                unsatBuffer, wedgeBuffer, cancellationToken);
            return outcome == SelectionOutcome.Selected ? 1 : 0;
        }

        return 0;
    }
}
