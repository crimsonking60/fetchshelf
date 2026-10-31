using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Takes started selections off the Selected queue once their torrent has finished downloading.
/// A selection is only the request to download; from the moment it starts, the Torrents list is the record of the
/// download. Left in place, a finished selection would keep counting as bytes in flight against the ratio budget.
/// </summary>
public static class SelectionRetirement
{
    /// <summary>
    /// Marks every started, not yet removed selection for the given MAM ids as removed now. The caller saves.
    /// Returns how many were retired.
    /// </summary>
    public static async Task<int> RetireStartedAsync(
        FetchshelfDbContext dbContext, IReadOnlyCollection<long> finishedMamIds, CancellationToken cancellationToken = default)
    {
        if (finishedMamIds.Count == 0)
        {
            return 0;
        }

        var selections = await dbContext.SelectedTorrents
            .Where(s => s.StartedAt != null && s.RemovedAt == null && finishedMamIds.Contains(s.MamId))
            .ToListAsync(cancellationToken);

        var retiredAt = DateTimeOffset.UtcNow;
        foreach (var selection in selections)
        {
            selection.RemovedAt = retiredAt;
        }

        return selections.Count;
    }
}
