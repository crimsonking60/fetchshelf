using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data;

/// <summary>
/// Keeps each torrent's people and series rows and its <see cref="TorrentEntity.Language"/> and
/// <see cref="TorrentEntity.MediaType"/> in step with its metadata JSON. Runs inside every save of
/// <see cref="FetchshelfDbContext"/>, on the torrents that save adds or whose <see cref="TorrentEntity.MetadataJson"/>
/// it changes, so no code that writes metadata has to remember it. The new rows are saved in the same transaction as
/// the torrent, so a failed save leaves the old ones.
/// </summary>
internal static class TorrentProjectionSync
{
    public static async Task ApplyAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken, bool async)
    {
        var changed = dbContext.ChangeTracker.Entries<TorrentEntity>()
            .Where(e => e.State == EntityState.Added
                || (e.State == EntityState.Modified && e.Property(t => t.MetadataJson).IsModified))
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var ids = changed.Select(e => e.Entity.Id).ToHashSet();

        // Rows added by an earlier save attempt that failed are still tracked; they are made again below.
        foreach (var stale in dbContext.ChangeTracker.Entries<TorrentPersonEntity>()
            .Where(e => e.State == EntityState.Added && ids.Contains(e.Entity.TorrentId)).ToList())
        {
            stale.State = EntityState.Detached;
        }

        foreach (var stale in dbContext.ChangeTracker.Entries<TorrentSeriesEntity>()
            .Where(e => e.State == EntityState.Added && ids.Contains(e.Entity.TorrentId)).ToList())
        {
            stale.State = EntityState.Detached;
        }

        var modifiedIds = changed.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id).ToList();
        if (modifiedIds.Count > 0)
        {
            var oldPeople = dbContext.TorrentPeople.Where(p => modifiedIds.Contains(p.TorrentId));
            var oldSeries = dbContext.TorrentSeries.Where(s => modifiedIds.Contains(s.TorrentId));
            dbContext.TorrentPeople.RemoveRange(async ? await oldPeople.ToListAsync(cancellationToken) : oldPeople.ToList());
            dbContext.TorrentSeries.RemoveRange(async ? await oldSeries.ToListAsync(cancellationToken) : oldSeries.ToList());
        }

        foreach (var torrent in changed.Select(e => e.Entity))
        {
            TorrentProjection projection;
            try
            {
                projection = TorrentProjection.From(TorrentMetadataSerializer.Deserialize(torrent.MetadataJson));
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
            {
                // The torrent is still saved: its author and series rows must never be what stops a save.
                dbContext.GetService<ILoggerFactory>().CreateLogger(typeof(TorrentProjectionSync))
                    .LogWarning("The metadata of torrent {TorrentId} could not be read, so it has no author or series rows: {Error}", torrent.Id, ex.Message);
                torrent.Language = null;
                torrent.MediaType = null;
                continue;
            }

            torrent.Language = projection.Language;
            torrent.MediaType = projection.MediaType;
            dbContext.TorrentPeople.AddRange(projection.People.Select(p => new TorrentPersonEntity
            {
                TorrentId = torrent.Id,
                Role = p.Role,
                Position = p.Position,
                Name = p.Name,
                NameKey = p.NameKey,
            }));
            dbContext.TorrentSeries.AddRange(projection.Series.Select(s => new TorrentSeriesEntity
            {
                TorrentId = torrent.Id,
                Position = s.Position,
                Name = s.Name,
                NameKey = s.NameKey,
                Kind = s.Kind,
                FirstNumber = s.FirstNumber is { } first ? (double)first : null,
                LastNumber = s.LastNumber is { } last ? (double)last : null,
                Display = s.Display,
            }));
        }
    }
}
