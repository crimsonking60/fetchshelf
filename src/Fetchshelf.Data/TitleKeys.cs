using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Recomputes the stored title keys (<see cref="TitleNormalizer"/>) of every torrent and selection, for when the rule
/// that makes them changes. Torrents are keyed from their stored title, selections from the title in their metadata.
/// Works in batches with the change tracker cleared between them, so a large library doesn't pile up in memory.
/// </summary>
public static class TitleKeys
{
    private const int BatchSize = 500;

    /// <returns>How many torrent and selection keys changed.</returns>
    public static (int Torrents, int Selections) RecomputeAll(FetchshelfDbContext dbContext)
    {
        var torrents = Recompute(dbContext, dbContext.Torrents, t => t.Id, t => TitleNormalizer.Normalize(t.Title),
            t => t.NormalizedTitleKey, (t, key) => t.NormalizedTitleKey = key);
        var selections = Recompute(dbContext, dbContext.SelectedTorrents, s => s.MamId,
            s => SelectionTitle(s.MetadataJson) is { } title ? TitleNormalizer.Normalize(title) : s.NormalizedTitleKey,
            s => s.NormalizedTitleKey, (s, key) => s.NormalizedTitleKey = key);
        return (torrents, selections);
    }

    /// <summary>The title in a selection's stored metadata, or null when it can't be read; its key is then left alone.</summary>
    private static string? SelectionTitle(string metadataJson)
    {
        try
        {
            return TorrentMetadataSerializer.Deserialize(metadataJson).Title;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int Recompute<TEntity, TKey>(
        FetchshelfDbContext dbContext, DbSet<TEntity> set, System.Linq.Expressions.Expression<Func<TEntity, TKey>> orderBy,
        Func<TEntity, string> computeKey, Func<TEntity, string> storedKey, Action<TEntity, string> setKey)
        where TEntity : class
    {
        var changed = 0;
        for (var skip = 0; ; skip += BatchSize)
        {
            var batch = set.OrderBy(orderBy).Skip(skip).Take(BatchSize).ToList();
            foreach (var entity in batch)
            {
                var key = computeKey(entity);
                if (key != storedKey(entity))
                {
                    setKey(entity, key);
                    changed++;
                }
            }

            dbContext.SaveChanges();
            dbContext.ChangeTracker.Clear();
            if (batch.Count < BatchSize)
            {
                return changed;
            }
        }
    }
}
