using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Makes the people and series rows, language and media type of every stored torrent, for a database written before
/// they existed. Each torrent is saved as if its metadata had changed, so <see cref="TorrentProjectionSync"/> does the
/// work. Batches with the change tracker cleared between them, like <see cref="TitleKeys"/>.
/// </summary>
public static class TorrentProjectionBackfill
{
    private const int BatchSize = 500;

    /// <returns>How many torrents were projected.</returns>
    public static int RunAll(FetchshelfDbContext dbContext)
    {
        var projected = 0;
        for (var skip = 0; ; skip += BatchSize)
        {
            var batch = dbContext.Torrents.OrderBy(t => t.Id).Skip(skip).Take(BatchSize).ToList();
            foreach (var torrent in batch)
            {
                dbContext.Entry(torrent).Property(t => t.MetadataJson).IsModified = true;
            }

            dbContext.SaveChanges();
            dbContext.ChangeTracker.Clear();
            projected += batch.Count;
            if (batch.Count < BatchSize)
            {
                return projected;
            }
        }
    }
}
