using Fetchshelf.Core.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// One-shot maintenance operation: recomputes every stored Torrent's normalized title key. Touches only the
/// Torrents table — Selected torrents and Duplicate torrents are left untouched,
/// matching the reference's `update_search_title` (which only ever scans the Torrent table).
/// </summary>
public static class TitleMaintenanceRunner
{
    public static async Task RunAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var torrents = await dbContext.Torrents.ToListAsync(cancellationToken);
        foreach (var torrent in torrents)
        {
            torrent.NormalizedTitleKey = TitleNormalizer.Normalize(torrent.Title);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
