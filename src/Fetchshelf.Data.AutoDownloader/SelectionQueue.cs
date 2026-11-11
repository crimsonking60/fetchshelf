using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>The queries over selections that the Download job and the Account page share.</summary>
public static class SelectionQueue
{
    /// <summary>
    /// The total size of torrents started but not yet finished at a cost that spends ratio: MAM doesn't count them as
    /// downloaded yet, so the ratio budget takes them off itself.
    /// </summary>
    public static async Task<long> InFlightBytesAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var ratioSpendingCosts = Enum.GetValues<SelectedTorrentCost>().Where(DownloadBudget.SpendsRatio).Select(c => c.ToString()).ToList();
        var startedNonFree = await dbContext.SelectedTorrents
            .Where(s => s.StartedAt != null && s.RemovedAt == null)
            .Where(s => ratioSpendingCosts.Contains(s.CostDecision))
            .Select(s => s.MetadataJson)
            .ToListAsync(cancellationToken);

        return startedNonFree.Sum(json => TorrentMetadataSerializer.Deserialize(json).TotalSizeBytes);
    }

    /// <summary>
    /// The selections an autograb has waiting, which its `max_active_downloads` caps; 0 when it sets no cap. Selections
    /// are matched to an autograb by its name, which the configuration requires along with the cap.
    /// </summary>
    public static async Task<int> ActiveForAutograbAsync(FetchshelfDbContext dbContext, AutograbEntry entry, CancellationToken cancellationToken = default) =>
        entry.MaxActiveDownloads is null || string.IsNullOrWhiteSpace(entry.Name)
            ? 0
            : await dbContext.SelectedTorrents.CountAsync(
                s => s.GrabberName == entry.Name && s.RemovedAt == null && s.StartedAt == null, cancellationToken);

    /// <summary>The selections waiting to be downloaded, oldest first, as the Download job takes them.</summary>
    public static async Task<List<Entities.SelectedTorrentEntity>> PendingAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default) =>
        // SQLite's EF provider can't translate ORDER BY on a DateTimeOffset column, so order client-side.
        (await dbContext.SelectedTorrents
            .Where(s => s.StartedAt == null && s.RemovedAt == null)
            .ToListAsync(cancellationToken))
            .OrderBy(s => s.CreatedAt)
            .ToList();
}
