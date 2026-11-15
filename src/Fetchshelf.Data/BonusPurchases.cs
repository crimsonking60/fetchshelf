using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>The bonus purchases the event log records.</summary>
public static class BonusPurchases
{
    /// <summary>When a wedge was last bought with points; null if never. Events are appended in order, so the latest has the highest id.</summary>
    public static async Task<DateTimeOffset?> LastWedgeAtAsync(FetchshelfDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var wedge = nameof(BonusPurchaseKind.Wedge);
        var last = await dbContext.Events
            .Where(e => e.Kind == EventKind.BonusPurchase && e.BonusSpend == wedge)
            .OrderByDescending(e => e.Id)
            .Select(e => (DateTimeOffset?)e.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
        return last;
    }

    /// <summary>The latest <paramref name="count"/> purchases, newest first.</summary>
    public static Task<List<EventEntity>> RecentAsync(FetchshelfDbContext dbContext, int count, CancellationToken cancellationToken = default) =>
        dbContext.Events
            .AsNoTracking()
            .Where(e => e.Kind == EventKind.BonusPurchase)
            .OrderByDescending(e => e.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
}
