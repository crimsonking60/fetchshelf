using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Makes the people and series rows, language and media type of every stored torrent, for a database written before
/// they existed. Only the two columns and the new rows are written; the metadata JSON is read, not rewritten. Batches
/// with the change tracker cleared between them, like <see cref="TitleKeys"/>.
/// </summary>
public static class TorrentProjectionBackfill
{
    private const int BatchSize = 500;

    /// <returns>How many torrents were projected.</returns>
    public static int RunAll(FetchshelfDbContext dbContext)
    {
        // Whatever rows there are get made again, so running this twice gives the same rows.
        dbContext.TorrentPeople.ExecuteDelete();
        dbContext.TorrentSeries.ExecuteDelete();

        var logger = TorrentProjectionSync.Logger(dbContext);
        var projected = 0;
        for (var skip = 0; ; skip += BatchSize)
        {
            var batch = dbContext.Torrents.OrderBy(t => t.Id).Skip(skip).Take(BatchSize).ToList();
            foreach (var torrent in batch)
            {
                var (people, series) = TorrentProjectionSync.Project(dbContext.Entry(torrent), logger);
                dbContext.TorrentPeople.AddRange(people);
                dbContext.TorrentSeries.AddRange(series);
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
