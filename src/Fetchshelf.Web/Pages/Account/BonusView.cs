using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Account;

/// <summary>The Account page's Bonus points section: the `[bonus]` settings, what the next run would buy, the last purchases.</summary>
/// <param name="Settings">The `[bonus]` section; null when there is none and nothing is bought.</param>
/// <param name="Points">The points as MAM last gave them; null before the first read.</param>
/// <param name="NextRun">What the next run would buy from <paramref name="Points"/>; null without both.</param>
/// <param name="NextWedgeDue">When the next wedge is due; null when wedges aren't bought.</param>
/// <param name="Recent">The latest purchases, newest first.</param>
public sealed record BonusView(
    BonusSettings? Settings, double? Points, BonusRunPlan? NextRun, DateTimeOffset? NextWedgeDue, IReadOnlyList<EventEntity> Recent)
{
    private const int RecentShown = 5;

    /// <summary>The upload credit the points would buy, in whole GB, for the hint shown without a `[bonus]` section.</summary>
    public long? UploadGbThePointsBuy => Points is { } points ? (long)(points / BonusPurchase.PointsPerUploadGb) : null;

    public static async Task<BonusView> BuildAsync(
        BonusSettings? settings, double? points, FetchshelfDbContext dbContext, TimeProvider time, CancellationToken cancellationToken)
    {
        var recent = await BonusPurchases.RecentAsync(dbContext, RecentShown, cancellationToken);
        if (settings is null)
        {
            return new BonusView(null, points, null, null, recent);
        }

        var policy = settings.ToPolicy();
        var lastWedgeAt = await BonusPurchases.LastWedgeAtAsync(dbContext, cancellationToken);
        var nextRun = points is { } known ? BonusPlanner.PlanRun(policy, new BonusRunState(known, time.GetUtcNow(), lastWedgeAt)) : null;
        var nextWedgeDue = BonusPlanner.NextWedgeDue(policy, lastWedgeAt) is { } due && due > DateTimeOffset.MinValue ? due : (DateTimeOffset?)null;
        return new BonusView(settings, points, nextRun, nextWedgeDue, recent);
    }
}
