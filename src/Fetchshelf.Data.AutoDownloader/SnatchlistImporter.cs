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
/// Imports a `[[snatchlist]]` entry: pages the user's MAM snatch list and refreshes the metadata of
/// torrents already stored. With `metadata_only_add` it also creates metadata-only Torrent records for
/// the others, so those torrents count as owned for duplicate detection.
/// </summary>
public sealed class SnatchlistImporter(
    FetchshelfDbContext dbContext, MamClient mamClient, MamAccessGate accessGate, ILogger<SnatchlistImporter>? logger = null)
{
    private const long RowsPerPage = 250;

    private readonly ILogger<SnatchlistImporter> _logger = logger ?? NullLogger<SnatchlistImporter>.Instance;

    /// <summary>Returns the number of new metadata-only entries created; a dry run creates none and returns 0.</summary>
    public async Task<int> RunAsync(SnatchlistEntry entry, CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var kind = ToMamKind(entry.Type);
        var cacheTimestamp = DateTimeOffset.UtcNow;
        var maxPages = entry.MaxPages ?? 50;
        var name = entry.Name ?? entry.Type.ToString();
        var detailLevel = entry.DryRun ? LogLevel.Information : LogLevel.Debug;
        var createdCount = 0;
        var rowCount = 0;
        if (entry.DryRun)
        {
            _logger.LogInformation("Snatchlist {Name}: dry run, nothing will be written", name);
        }

        for (var page = 0; page < maxPages; page++)
        {
            var rows = await mamClient.GetSnatchListAsync(kind, page, cacheTimestamp, cancellationToken);
            if (rows.Count == 0)
            {
                break;
            }

            rowCount += rows.Count;
            foreach (var row in rows)
            {
                if (await ImportRowAsync(name, row, filter, entry.Cost, entry.DryRun, detailLevel, cancellationToken))
                {
                    createdCount++;
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

        _logger.LogInformation(
            "Snatchlist {Name}: read {RowCount} snatch list rows, {CreatedCount} {Verb}",
            name, rowCount, createdCount, entry.DryRun ? "would have been recorded as owned (dry run)" : "recorded as owned");
        return entry.DryRun ? 0 : createdCount;
    }

    private async Task<bool> ImportRowAsync(
        string name, MamSnatchlistRow row, TorrentFilter filter, ConfiguredCost cost, bool dryRun, LogLevel detailLevel,
        CancellationToken cancellationToken)
    {
        // The snatch list row has no uploader or completion count, so the filter is checked against the search record.
        var result = await mamClient.SearchAsync(new MamSearchQuery { Id = row.MamId }, cancellationToken);
        if (result.Data.FirstOrDefault() is not { } record)
        {
            _logger.Log(detailLevel, "Snatchlist {Name}: MAM torrent {MamId} was not found on MAM, skipped", name, row.MamId);
            return false;
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
            _logger.Log(detailLevel, "Snatchlist {Name}: MAM torrent {MamId} \"{Title}\" does not pass the filter, skipped", name, row.MamId, metadata.Title);
            return false;
        }

        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == row.MamId, cancellationToken);
        if (existing is not null)
        {
            _logger.Log(
                detailLevel, "Snatchlist {Name}: MAM torrent {MamId} \"{Title}\" is already known, {Action}",
                name, row.MamId, metadata.Title, dryRun ? "its metadata would be refreshed" : "metadata refreshed");
            if (dryRun)
            {
                return false;
            }

            existing.MetadataJson = TorrentMetadataSerializer.Serialize(metadata);
            existing.Title = metadata.Title;
            existing.NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title);
            return false;
        }

        if (cost != ConfiguredCost.MetadataOnlyAdd)
        {
            _logger.Log(
                detailLevel, "Snatchlist {Name}: MAM torrent {MamId} \"{Title}\" is not stored, skipped (cost metadata_only only updates stored torrents)",
                name, row.MamId, metadata.Title);
            return false;
        }

        _logger.Log(
            detailLevel, "Snatchlist {Name}: MAM torrent {MamId} \"{Title}\" {Action}",
            name, row.MamId, metadata.Title, dryRun ? "would be recorded as owned" : "recorded as owned");
        if (dryRun)
        {
            return true;
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
        return true;
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
