using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.Notion;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger<NotionListImporter>? logger = null)
{
    private static readonly TimeSpan PolitenessDelay = TimeSpan.FromMilliseconds(400);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));
    private readonly ILogger<NotionListImporter> _logger = logger ?? NullLogger<NotionListImporter>.Instance;

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
            _logger.LogInformation(
                "Notion list {ListName}: no unsatisfied-snatch slots free beyond the buffer of {UnsatBuffer}, so nothing was looked up",
                entry.Name, unsatBuffer);
            return 0;
        }

        var rows = await notionClient.QueryAllRowsAsync(entry.DataSource, entry.Token, cancellationToken);
        var mamIds = rows.SelectMany(row => NotionMamIdExtractor.ExtractMamIds(row, entry.MamFields)).ToList();
        _logger.LogInformation(
            "Notion list {ListName}: read {RowCount} rows with {LinkCount} MaM links{DryRun}",
            entry.Name, rows.Count, mamIds.Count, entry.DryRun ? " (dry run: nothing will be selected)" : string.Empty);

        var grabFilters = entry.Grab.Select(SharedFilterConfigMapper.ToTorrentFilter).ToList();
        var detailLevel = entry.DryRun ? LogLevel.Information : LogLevel.Debug;
        var selectedCount = 0;
        var wouldSelectCount = 0;
        foreach (var mamId in mamIds)
        {
            if (selectedCount + wouldSelectCount >= budget)
            {
                _logger.LogInformation("Notion list {ListName}: stopped because the run's limit of {Budget} selections was reached", entry.Name, budget);
                break;
            }

            var alreadyKnown = await dbContext.Torrents.AnyAsync(t => t.MamId == mamId, cancellationToken);
            if (alreadyKnown)
            {
                _logger.Log(detailLevel, "Notion list {ListName}: MaM torrent {MamId} is already known, skipped", entry.Name, mamId);
                continue;
            }

            var outcome = await ProcessMamIdAsync(entry, grabFilters, mamId, ignoreTorrents, unsatBuffer, wedgeBuffer, detailLevel, cancellationToken);
            selectedCount += outcome == ProcessOutcome.Selected ? 1 : 0;
            wouldSelectCount += outcome == ProcessOutcome.WouldSelect ? 1 : 0;

            await _delay(PolitenessDelay, cancellationToken);
        }

        if (entry.DryRun)
        {
            _logger.LogInformation(
                "Notion list {ListName}: dry run finished, {Count} torrents would have been selected", entry.Name, wouldSelectCount);
        }

        return selectedCount;
    }

    private async Task<ProcessOutcome> ProcessMamIdAsync(
        NotionListEntry entry,
        IReadOnlyList<TorrentFilter> grabFilters,
        long mamId,
        IReadOnlyList<long> ignoreTorrents,
        int unsatBuffer,
        int wedgeBuffer,
        LogLevel detailLevel,
        CancellationToken cancellationToken)
    {
        var searchResult = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId, ReturnDownloadLink = true }, cancellationToken);
        var record = searchResult.Data.FirstOrDefault();
        if (record is null)
        {
            _logger.Log(detailLevel, "Notion list {ListName}: MaM torrent {MamId} was not found on MaM, skipped", entry.Name, mamId);
            return ProcessOutcome.Skipped;
        }

        TorrentMetadata metadata;
        try
        {
            metadata = MamTorrentMapper.Map(record);
        }
        catch (UnrecognizedReferenceDataException ex)
        {
            _logger.Log(detailLevel, "Notion list {ListName}: MaM torrent {MamId} could not be read ({Reason}), skipped", entry.Name, mamId, ex.Message);
            return ProcessOutcome.Skipped;
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

            var outcome = await selectionRunner.ProcessCandidateAsync(
                record, metadata, ignoreTorrents, entry.Grab[i].Cost, autogrebCategory: null, grabberName: entry.Name,
                unsatBuffer, wedgeBuffer, dryRun: entry.DryRun, cancellationToken: cancellationToken);
            _logger.Log(
                detailLevel, "Notion list {ListName}: MaM torrent {MamId} \"{Title}\", grab block {BlockNumber} (cost {Cost}): {Outcome}",
                entry.Name, mamId, metadata.Title, i + 1, entry.Grab[i].Cost, outcome.Describe(entry.DryRun));
            return outcome != SelectionOutcome.Selected ? ProcessOutcome.Skipped
                : entry.DryRun ? ProcessOutcome.WouldSelect
                : ProcessOutcome.Selected;
        }

        _logger.Log(detailLevel, "Notion list {ListName}: MaM torrent {MamId} \"{Title}\" matches no grab block, skipped", entry.Name, mamId, metadata.Title);
        return ProcessOutcome.Skipped;
    }

    private enum ProcessOutcome
    {
        Skipped,
        Selected,
        WouldSelect,
    }
}
