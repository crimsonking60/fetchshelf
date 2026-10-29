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
/// Imports a `[[snatchlist]]` entry: pages the user's MaM snatch list and creates/updates
/// metadata-only Torrent records so those torrents count as owned for duplicate detection,
/// Fetchshelf-spec.md §7.7.
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
                if (await ImportRowAsync(name, row, filter, entry.DryRun, detailLevel, cancellationToken))
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
        string name, MamSnatchlistRow row, TorrentFilter filter, bool dryRun, LogLevel detailLevel, CancellationToken cancellationToken)
    {
        var metadata = await mamClient.LookupAsync(row.MamId, cancellationToken);
        if (metadata is null)
        {
            _logger.Log(detailLevel, "Snatchlist {Name}: MaM torrent {MamId} was not found on MaM, skipped", name, row.MamId);
            return false;
        }

        var candidate = new TorrentFilterCandidate
        {
            Metadata = metadata,
            UploaderName = string.Empty,
            Seeders = row.Seeders,
            Leechers = row.Leechers,
            TimesCompleted = 0,
        };
        if (!TorrentFilterMatcher.Matches(filter, candidate))
        {
            _logger.Log(detailLevel, "Snatchlist {Name}: MaM torrent {MamId} \"{Title}\" does not pass the filter, skipped", name, row.MamId, metadata.Title);
            return false;
        }

        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == row.MamId, cancellationToken);
        if (existing is not null)
        {
            _logger.Log(
                detailLevel, "Snatchlist {Name}: MaM torrent {MamId} \"{Title}\" is already known, {Action}",
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

        _logger.Log(
            detailLevel, "Snatchlist {Name}: MaM torrent {MamId} \"{Title}\" {Action}",
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
