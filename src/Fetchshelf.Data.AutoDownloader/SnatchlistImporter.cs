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
/// changed, and rows that could not be imported. In a dry run, which writes nothing, the counts are what would have
/// happened.
/// </summary>
public sealed record SnatchlistRunSummary(int Rows, int Recorded, int Updated, int Failed, bool DryRun = false);

/// <summary>
/// Imports a `[[snatchlist]]` entry: pages the user's MAM snatch list and refreshes the metadata of
/// torrents already stored. With `metadata_only_add` it also creates metadata-only Torrent records for
/// the others, so those torrents count as owned for duplicate detection.
/// </summary>
public sealed class SnatchlistImporter(
    FetchshelfDbContext dbContext, MamClient mamClient, MamAccessGate accessGate, ILogger<SnatchlistImporter>? logger = null)
{
    private readonly ILogger<SnatchlistImporter> _logger = logger ?? NullLogger<SnatchlistImporter>.Instance;

    /// <summary>
    /// Reads the snatch list and imports each row. A row that cannot be imported is logged and the run continues;
    /// MAM refusing the account (rate limit, block, 403) ends the run, since every later request would fail too.
    /// </summary>
    public async Task<SnatchlistRunSummary> RunAsync(SnatchlistEntry entry, CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync($"Snatchlist {entry.Name ?? entry.Type.ToString()}", cancellationToken);

        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var kind = ToMamKind(entry.Type);
        var cacheTimestamp = DateTimeOffset.UtcNow;
        var maxPages = entry.MaxPages ?? 50;
        var name = entry.Name ?? entry.Type.ToString();
        var detailLevel = entry.DryRun ? LogLevel.Information : LogLevel.Debug;
        var (rowCount, recorded, updated, failed, pagesRead) = (0, 0, 0, 0, 0);
        var stopped = false;

        // MAM decides how many rows a page holds, and may send rows it already sent: paging stops at an empty page, a
        // page with nothing new, or a page shorter than the first.
        var seenMamIds = new HashSet<long>();
        int? firstPageSize = null;
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

            var newRows = rows.Where(row => seenMamIds.Add(row.MamId)).ToList();
            _logger.LogDebug("Snatchlist {Name}: page {Page} has {RowCount} rows, {NewCount} not seen before", name, page + 1, rows.Count, newRows.Count);
            if (newRows.Count == 0)
            {
                break;
            }

            pagesRead++;
            rowCount += newRows.Count;
            foreach (var row in newRows)
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

            firstPageSize ??= rows.Count;
            if (rows.Count < firstPageSize)
            {
                break;
            }
        }

        LogSummary(name, entry.DryRun, rowCount, pagesRead, recorded, updated, failed);
        return new SnatchlistRunSummary(rowCount, recorded, updated, failed, entry.DryRun);
    }

    private void LogSummary(string name, bool dryRun, int rowCount, int pagesRead, int recorded, int updated, int failed)
    {
        if (rowCount == 0 && failed == 0)
        {
            _logger.LogInformation("Snatchlist {Name}: the snatch list is empty, nothing to do", name);
            return;
        }

        if (recorded == 0 && updated == 0 && failed == 0)
        {
            _logger.LogInformation(
                "Snatchlist {Name}: read {RowCount} snatch list rows from {Pages}, nothing to do: everything is up to date",
                name, rowCount, PagesText(pagesRead));
            return;
        }

        var failedNote = failed > 0 ? $", {failed} failed" : string.Empty;
        _logger.LogInformation(
            "Snatchlist {Name}: read {RowCount} snatch list rows from {Pages}, {Recorded} {RecordedVerb}, {Updated} {UpdatedVerb}{FailedNote}",
            name, rowCount, PagesText(pagesRead), recorded, dryRun ? "would have been recorded as owned (dry run)" : "recorded as owned",
            updated, dryRun ? "would have had their metadata updated" : "had their metadata updated", failedNote);
    }

    private static string PagesText(int pages) => pages == 1 ? "1 page" : $"{pages} pages";

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
        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == row.MamId, cancellationToken);
        if (existing is null && cost != ConfiguredCost.MetadataOnlyAdd)
        {
            _logger.Log(
                detailLevel, "Snatchlist {Name}: {Title} (MAM id {MamId}) is not stored, skipped (cost metadata_only only updates stored torrents)",
                name, row.Title, row.MamId);
            return RowOutcome.Skipped;
        }

        var stored = existing is null ? null : TorrentMetadataSerializer.Deserialize(existing.MetadataJson);
        var candidate = await ReadCandidateAsync(name, row, stored, detailLevel, cancellationToken);
        if (candidate is null)
        {
            return RowOutcome.Skipped;
        }

        var metadata = candidate.Metadata;
        if (!TorrentFilterMatcher.Matches(filter, candidate))
        {
            _logger.Log(detailLevel, "Snatchlist {Name}: {Torrent} does not pass the filter, skipped", name, metadata.DisplayName());
            return RowOutcome.Skipped;
        }

        if (existing is not null)
        {
            return Refresh(name, existing, stored!, metadata, dryRun, detailLevel);
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

    /// <summary>
    /// The torrent's current metadata and filter fields. A stored torrent is refreshed from its row, which needs no
    /// request; a new torrent, or a row without metadata, is looked up on MAM, since the row lacks the language.
    /// Null when MAM no longer has the torrent.
    /// </summary>
    private async Task<TorrentFilterCandidate?> ReadCandidateAsync(
        string name, MamSnatchlistRow row, TorrentMetadata? stored, LogLevel detailLevel, CancellationToken cancellationToken)
    {
        if (stored is not null && row.Details is { } details)
        {
            return new TorrentFilterCandidate
            {
                Metadata = MamSnatchlistRowMapper.Map(row, details, stored),
                UploaderName = details.UploaderName,
                Seeders = row.Seeders,
                Leechers = row.Leechers,
                TimesCompleted = details.TimesCompleted,
            };
        }

        var result = await mamClient.SearchAsync(new MamSearchQuery { Id = row.MamId }, cancellationToken);
        if (result.Data.FirstOrDefault() is not { } record)
        {
            _logger.Log(detailLevel, "Snatchlist {Name}: {Title} (MAM id {MamId}) was not found on MAM, skipped", name, row.Title, row.MamId);
            return null;
        }

        return new TorrentFilterCandidate
        {
            Metadata = MamTorrentMapper.Map(record),
            UploaderName = record.UploaderName,
            Seeders = record.Seeders,
            Leechers = record.Leechers,
            TimesCompleted = record.TimesCompleted,
        };
    }

    /// <summary>
    /// Stores <paramref name="fresh"/> over a stored torrent's metadata when it differs. Metadata edited by hand is
    /// kept: only its VIP status follows MAM.
    /// </summary>
    private RowOutcome Refresh(string name, TorrentEntity existing, TorrentMetadata stored, TorrentMetadata fresh, bool dryRun, LogLevel detailLevel)
    {
        var editedByHand = HandEdits.AreIn(stored);
        var updated = HandEdits.KeepOver(stored, fresh);
        var updatedJson = TorrentMetadataSerializer.Serialize(updated);
        if (updatedJson == existing.MetadataJson)
        {
            _logger.Log(
                detailLevel, "Snatchlist {Name}: {Torrent} is already up to date{Note}",
                name, stored.DisplayName(), editedByHand ? " (its metadata was edited by hand and is kept)" : string.Empty);
            return RowOutcome.Unchanged;
        }

        _logger.LogInformation(
            "Snatchlist {Name}: {Torrent} {Action}",
            name, updated.DisplayName(),
            (dryRun, editedByHand) switch
            {
                (true, true) => "would have its VIP status updated from MAM; its metadata was edited by hand and is kept",
                (false, true) => "had its VIP status updated from MAM; its metadata was edited by hand and is kept",
                (true, false) => "would have its metadata updated from MAM",
                (false, false) => "had its metadata updated from MAM",
            });
        if (!dryRun)
        {
            existing.MetadataJson = updatedJson;
            existing.Title = updated.Title;
            existing.NormalizedTitleKey = TitleNormalizer.Normalize(updated.Title);
        }

        return RowOutcome.Updated;
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
