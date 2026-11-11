using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data;

/// <summary>
/// Keeps each torrent's people and series rows and its <see cref="TorrentEntity.Language"/> and
/// <see cref="TorrentEntity.MediaType"/> in step with its metadata JSON. <see cref="FetchshelfDbContext"/> runs it in
/// every save, on the torrents that save adds or whose <see cref="TorrentEntity.MetadataJson"/> it changes, so no code
/// that writes metadata has to remember it.
/// </summary>
internal static class TorrentProjectionSync
{
    /// <summary>
    /// Sets the columns and adds the new rows for the torrents this save adds or changes the metadata of. Returns the
    /// torrents whose old rows the save must delete first; the context deletes them by torrent, in the save's own
    /// transaction, so two saves of one torrent at once both succeed (the last one wins) and a failed save keeps them.
    /// Expects the change tracker's changes to be detected already.
    /// </summary>
    public static IReadOnlyList<string> Prepare(FetchshelfDbContext dbContext)
    {
        // Rows made for an earlier save of this context that failed: made again below if their torrent still needs them.
        foreach (var stale in dbContext.PendingProjectionRows)
        {
            if (dbContext.Entry(stale).State == EntityState.Added)
            {
                dbContext.Entry(stale).State = EntityState.Detached;
            }
        }

        dbContext.PendingProjectionRows.Clear();

        var changed = dbContext.ChangeTracker.Entries<TorrentEntity>()
            .Where(e => e.State == EntityState.Added
                || (e.State == EntityState.Modified && e.Property(t => t.MetadataJson).IsModified))
            .ToList();
        if (changed.Count == 0)
        {
            return [];
        }

        var replaced = changed.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id).ToList();
        DetachLoadedRows(dbContext, replaced.ToHashSet());

        var logger = Logger(dbContext);
        foreach (var entry in changed)
        {
            var (people, series) = Project(entry, logger);
            dbContext.TorrentPeople.AddRange(people);
            dbContext.TorrentSeries.AddRange(series);
            dbContext.PendingProjectionRows.AddRange(people);
            dbContext.PendingProjectionRows.AddRange(series);
        }

        return replaced;
    }

    /// <summary>
    /// Sets one tracked torrent's language and media type from its metadata JSON and makes its people and series rows,
    /// without adding them. Metadata that can't be read, or projected, gives no rows and null columns, and a warning:
    /// these rows must never be what stops a save, or the startup upgrade.
    /// </summary>
    public static (List<TorrentPersonEntity> People, List<TorrentSeriesEntity> Series) Project(EntityEntry<TorrentEntity> entry, ILogger logger)
    {
        var torrent = entry.Entity;
        TorrentProjection projection;
        try
        {
            projection = TorrentProjection.From(TorrentMetadataSerializer.Deserialize(torrent.MetadataJson));
        }
        catch (Exception ex)
        {
            logger.LogWarning("The metadata of torrent {TorrentId} could not be read, so it has no author or series rows: {Error}", torrent.Id, ex.Message);
            Set(entry, null, null);
            return ([], []);
        }

        Set(entry, projection.Language, projection.MediaType);
        var people = projection.People.Select(p => new TorrentPersonEntity
        {
            TorrentId = torrent.Id,
            Role = p.Role,
            Position = p.Position,
            Name = p.Name,
            NameKey = p.NameKey,
        }).ToList();
        var series = projection.Series.Select(s => new TorrentSeriesEntity
        {
            TorrentId = torrent.Id,
            Position = s.Position,
            Name = s.Name,
            NameKey = s.NameKey,
            Kind = s.Kind,
            FirstNumber = s.FirstNumber is { } first ? (double)first : null,
            LastNumber = s.LastNumber is { } last ? (double)last : null,
            Display = s.Display,
        }).ToList();
        return (people, series);
    }

    public static ILogger Logger(FetchshelfDbContext dbContext) =>
        dbContext.GetService<ILoggerFactory>().CreateLogger(typeof(TorrentProjectionSync));

    /// <summary>Through the entry, so the change is seen even when the save has stopped detecting changes itself.</summary>
    private static void Set(EntityEntry<TorrentEntity> entry, string? language, string? mediaType)
    {
        entry.Property(t => t.Language).CurrentValue = language;
        entry.Property(t => t.MediaType).CurrentValue = mediaType;
    }

    /// <summary>Old rows of these torrents loaded into this context are deleted by the save without it, so they are let go.</summary>
    private static void DetachLoadedRows(FetchshelfDbContext dbContext, HashSet<string> torrentIds)
    {
        if (torrentIds.Count == 0)
        {
            return;
        }

        foreach (var row in dbContext.ChangeTracker.Entries<TorrentPersonEntity>().Where(e => torrentIds.Contains(e.Entity.TorrentId)).ToList())
        {
            row.State = EntityState.Detached;
        }

        foreach (var row in dbContext.ChangeTracker.Entries<TorrentSeriesEntity>().Where(e => torrentIds.Contains(e.Entity.TorrentId)).ToList())
        {
            row.State = EntityState.Detached;
        }
    }
}
