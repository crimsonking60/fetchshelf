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

/// <summary>
/// What one snatch list run did: rows read, torrents newly recorded as owned, stored torrents whose metadata
/// changed, and rows that could not be imported. In a dry run, the counts are what would have happened.
/// </summary>
public sealed record SnatchlistRunSummary(int Rows, int Recorded, int Updated, int Failed);

/// <summary>
/// Imports a `[[snatchlist]]` entry: pages the user's MAM snatch list and refreshes the metadata of
/// torrents already stored. With `metadata_only_add` it also creates metadata-only Torrent records for
/// the others, so those torrents count as owned for duplicate detection.
/// </summary>
public sealed class SnatchlistImporter(
    FetchshelfDbContext dbContext, MamClient mamClient, MamAccessGate accessGate, ILogger<SnatchlistImporter>? logger = null)
{
    private const long RowsPerPage = 250;

    private readonly ILogger<SnatchlistImporter> _logger = logger ?? NullLogger<SnatchlistImporter>.Instance;

    /// <summary>
    /// Reads the snatch list and imports each row. A row that cannot be imported is logged and the run continues;
    /// MAM refusing the account (rate limit, block, 403) ends the run, since every later request would fail too.
    /// </summary>
    public async Task<SnatchlistRunSummary> RunAsync(SnatchlistEntry entry, CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var kind = ToMamKind(entry.Type);
        var cacheTimestamp = DateTimeOffset.UtcNow;
        var maxPages = entry.MaxPages ?? 50;
        var name = entry.Name ?? entry.Type.ToString();
        var detailLevel = entry.DryRun ? LogLevel.Information : LogLevel.Debug;
        var (rowCount, recorded, updated, failed) = (0, 0, 0, 0);
        var stopped = false;
        if (entry.DryRun)
        {
            _logger.LogInformation("Snatchlist {Name}: dry run, nothing will be written", name);
        }

        for (var page = 0; page < maxPages && !stopped; page++)
        {
            IReadOnlyList<MamSnatchlistRow> rows;
            try
            {
                rows = await mamClient.GetSnatchListAsync(kind, page, cacheTimestamp, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Snatchlist {Name}: could not read page {Page} of the snatch list from MAM: {Error}", name, page + 1, ex.Message);
                throw;
            }

            if (rows.Count == 0)
            {
                break;
            }

            rowCount += rows.Count;
            foreach (var row in rows)
            {
                RowOutcome outcome;
                try
                {
                    outcome = await ImportRowAsync(name, row, filter, entry.Cost, entry.DryRun, detailLevel, cancellationToken);
                }
                catch (Exception ex) when (ex is MamAccountBlockedException or MamRateLimitedException or MamForbiddenException)
                {
                    _logger.LogError("Snatchlist {Name}: stopped at MAM torrent {MamId}: {Error} The rest is read on the next run", name, row.MamId, ex.Message);
                    failed++;
                    stopped = true;
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError("Snatchlist {Name}: could not import MAM torrent {MamId}: {Error}", name, row.MamId, ex.Message);
                    failed++;
                    continue;
                }

                if (outcome == RowOutcome.Recorded)
                {
                    recorded++;
                }
                else if (outcome == RowOutcome.Updated)
                {
                    updated++;
                }
            }

            if (!entry.DryRun)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (rows.Count < RowsPerPage)
            {
                break;
            }
        }

        LogSummary(name, entry.DryRun, rowCount, recorded, updated, failed);
        return new SnatchlistRunSummary(rowCount, recorded, updated, failed);
    }

    private void LogSummary(string name, bool dryRun, int rowCount, int recorded, int updated, int failed)
    {
        if (rowCount == 0 && failed == 0)
        {
            _logger.LogInformation("Snatchlist {Name}: the snatch list is empty, nothing to do", name);
            return;
        }

        if (recorded == 0 && updated == 0 && failed == 0)
        {
            _logger.LogInformation("Snatchlist {Name}: read {RowCount} snatch list rows, nothing to do: everything is up to date", name, rowCount);
            return;
        }

        var failedNote = failed > 0 ? $", {failed} failed" : string.Empty;
        _logger.LogInformation(
            "Snatchlist {Name}: read {RowCount} snatch list rows, {Recorded} {RecordedVerb}, {Updated} {UpdatedVerb}{FailedNote}",
            name, rowCount, recorded, dryRun ? "would have been recorded as owned (dry run)" : "recorded as owned",
            updated, dryRun ? "would have had their metadata updated" : "had their metadata updated", failedNote);
    }

    private enum RowOutcome
    {
        Skipped,
        Unchanged,
        Updated,
        Recorded,
    }

    private async Task<RowOutcome> ImportRowAsync(
        string name, MamSnatchlistRow row, TorrentFilter filter, ConfiguredCost cost, bool dryRun, LogLevel detailLevel,
        CancellationToken cancellationToken)
    {
        // The snatch list row has no uploader or completion count, so the filter is checked against the search record.
        var result = await mamClient.SearchAsync(new MamSearchQuery { Id = row.MamId }, cancellationToken);
        if (result.Data.FirstOrDefault() is not { } record)
        {
            _logger.Log(detailLevel, "Snatchlist {Name}: MAM torrent {MamId} was not found on MAM, skipped", name, row.MamId);
            return RowOutcome.Skipped;
        }

        var metadata = MamTorrentMapper.Map(record);
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
            _logger.Log(detailLevel, "Snatchlist {Name}: {Torrent} does not pass the filter, skipped", name, metadata.DisplayName());
            return RowOutcome.Skipped;
        }

        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == row.MamId, cancellationToken);
        if (existing is not null)
        {
            var metadataJson = TorrentMetadataSerializer.Serialize(metadata);
            if (metadataJson == existing.MetadataJson)
            {
                _logger.Log(detailLevel, "Snatchlist {Name}: {Torrent} is already up to date", name, metadata.DisplayName());
                return RowOutcome.Unchanged;
            }

            _logger.LogInformation(
                "Snatchlist {Name}: {Torrent} {Action}",
                name, metadata.DisplayName(), dryRun ? "would have its metadata updated from MAM" : "had its metadata updated from MAM");
            if (!dryRun)
            {
                existing.MetadataJson = metadataJson;
                existing.Title = metadata.Title;
                existing.NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title);
            }

            return RowOutcome.Updated;
        }

        if (cost != ConfiguredCost.MetadataOnlyAdd)
        {
            _logger.Log(
                detailLevel, "Snatchlist {Name}: {Torrent} is not stored, skipped (cost metadata_only only updates stored torrents)",
                name, metadata.DisplayName());
            return RowOutcome.Skipped;
        }

        _logger.LogInformation(
            "Snatchlist {Name}: {Torrent} {Action}", name, metadata.DisplayName(), dryRun ? "would be recorded as owned" : "recorded as owned");
        if (dryRun)
        {
            return RowOutcome.Recorded;
        }

        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = MetadataOnlyTorrentId.For(row.MamId),
            MamId = row.MamId,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return RowOutcome.Recorded;
    }

    private static MamSnatchlistKind ToMamKind(SnatchlistType type) => type switch
    {
        SnatchlistType.Unsat => MamSnatchlistKind.Unsat,
        SnatchlistType.InactUnsat => MamSnatchlistKind.InactiveUnsat,
        SnatchlistType.SeedUnsat => MamSnatchlistKind.SeedingUnsat,
        SnatchlistType.SeedSat => MamSnatchlistKind.SeedingSatisfied,
        SnatchlistType.InactSat => MamSnatchlistKind.InactiveSatisfied,
        SnatchlistType.UploadsActive => MamSnatchlistKind.ActiveUploads,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
