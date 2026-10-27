using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Imports a `[[snatchlist]]` entry: pages the user's MaM snatch list and creates/updates
/// metadata-only Torrent records so those torrents count as owned for duplicate detection,
/// Fetchshelf-spec.md §7.7.
/// </summary>
public sealed class SnatchlistImporter(FetchshelfDbContext dbContext, MamClient mamClient, MamAccessGate accessGate)
{
    private const long RowsPerPage = 250;

    /// <summary>Returns the number of new metadata-only entries created.</summary>
    public async Task<int> RunAsync(SnatchlistEntry entry, CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        var filter = SharedFilterConfigMapper.ToTorrentFilter(entry);
        var kind = ToMamKind(entry.Type);
        var cacheTimestamp = DateTimeOffset.UtcNow;
        var maxPages = entry.MaxPages ?? 50;
        var createdCount = 0;

        for (var page = 0; page < maxPages; page++)
        {
            var rows = await mamClient.GetSnatchListAsync(kind, page, cacheTimestamp, cancellationToken);
            if (rows.Count == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                if (await ImportRowAsync(row, filter, cancellationToken))
                {
                    createdCount++;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (rows.Count < RowsPerPage)
            {
                break;
            }
        }

        return createdCount;
    }

    private async Task<bool> ImportRowAsync(MamSnatchlistRow row, TorrentFilter filter, CancellationToken cancellationToken)
    {
        var metadata = await mamClient.LookupAsync(row.MamId, cancellationToken);
        if (metadata is null)
        {
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
            return false;
        }

        var existing = await dbContext.Torrents.FirstOrDefaultAsync(t => t.MamId == row.MamId, cancellationToken);
        if (existing is not null)
        {
            existing.MetadataJson = TorrentMetadataSerializer.Serialize(metadata);
            existing.Title = metadata.Title;
            existing.NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title);
            return false;
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
