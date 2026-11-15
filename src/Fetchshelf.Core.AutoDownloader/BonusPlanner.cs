using System.Globalization;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>What bonus points buy.</summary>
public enum BonusPurchaseKind
{
    /// <summary>One freeleech wedge.</summary>
    Wedge,

    /// <summary>VIP, topped up to the most MAM allows.</summary>
    Vip,

    /// <summary>Upload credit, in whole GB.</summary>
    Upload,
}

/// <summary>One purchase with bonus points.</summary>
/// <param name="Kind">What is bought.</param>
/// <param name="UploadGb">For <see cref="BonusPurchaseKind.Upload"/>, how many GB; 0 otherwise.</param>
public sealed record BonusPurchase(BonusPurchaseKind Kind, int UploadGb = 0)
{
    /// <summary>MAM's price of a GB of upload credit.</summary>
    public const long PointsPerUploadGb = 500;

    /// <summary>MAM's price of a wedge.</summary>
    public const long WedgePoints = 50_000;

    /// <summary>
    /// The points VIP needs above `keep_points` before a top-up is tried: enough for the longest top-up, so it can't take
    /// the points below `keep_points`.
    /// </summary>
    public const long VipReservePoints = 15_000;

    public static BonusPurchase Wedge { get; } = new(BonusPurchaseKind.Wedge);

    public static BonusPurchase Vip { get; } = new(BonusPurchaseKind.Vip);

    public static BonusPurchase Upload(int gb) => new(BonusPurchaseKind.Upload, gb);

    /// <summary>What it costs; for VIP, the most it can cost.</summary>
    public long Price => Kind switch
    {
        BonusPurchaseKind.Wedge => WedgePoints,
        BonusPurchaseKind.Vip => VipReservePoints,
        _ => UploadGb * PointsPerUploadGb,
    };

    /// <summary>"a wedge", "a VIP top-up", "50 GB of upload credit".</summary>
    public string Description => Kind switch
    {
        BonusPurchaseKind.Wedge => "a wedge",
        BonusPurchaseKind.Vip => "a VIP top-up",
        _ => $"{UploadGb} GB of upload credit",
    };
}

/// <summary>The `[bonus]` settings the planner decides by.</summary>
/// <param name="KeepPoints">Upload credit and VIP are only bought with points above this; a wedge may use them.</param>
/// <param name="UploadAmountsGb">The GB one upload purchase buys, tried largest first; empty buys none.</param>
/// <param name="WedgeEveryHours">Hours between wedge purchases; 0 buys none.</param>
/// <param name="Vip">Whether VIP is topped up, once a run.</param>
/// <param name="MaxPurchasesPerRun">Purchases one run makes at most.</param>
public sealed record BonusPolicy(long KeepPoints, IReadOnlyList<int> UploadAmountsGb, int WedgeEveryHours, bool Vip, int MaxPurchasesPerRun);

/// <summary>Where a run stands.</summary>
/// <param name="Points">The account's points now.</param>
/// <param name="Now">The time now.</param>
/// <param name="LastWedgeAt">When a wedge was last bought with points; null if never.</param>
/// <param name="Purchases">Purchases this run has made so far.</param>
/// <param name="WedgeDone">Whether this run has bought, or given up on, a wedge.</param>
/// <param name="VipDone">Whether this run has topped up, or given up on, VIP.</param>
public sealed record BonusRunState(
    double Points, DateTimeOffset Now, DateTimeOffset? LastWedgeAt, int Purchases = 0, bool WedgeDone = false, bool VipDone = false);

/// <summary>The next purchase, or why there is none.</summary>
public sealed record BonusDecision(BonusPurchase? Purchase, string Reason);

/// <summary>A run's purchases as planned, assuming each goes through at its price, and why the run stops there.</summary>
public sealed record BonusRunPlan(IReadOnlyList<BonusPurchase> Purchases, string StopReason);

/// <summary>
/// Decides what bonus points buy, one purchase at a time, in the order a run makes them: a wedge when one is due, then
/// a VIP top-up, then upload credit, the largest amount first, as long as the points above `keep_points` pay for it.
/// </summary>
public static class BonusPlanner
{
    /// <summary>
    /// How much earlier than <see cref="BonusPolicy.WedgeEveryHours"/> a wedge counts as due, so a schedule drifting by
    /// a few minutes doesn't push each purchase back a whole run.
    /// </summary>
    public static readonly TimeSpan WedgeDueSlack = TimeSpan.FromMinutes(10);

    /// <summary>The next purchase for <paramref name="state"/>, or none and why.</summary>
    public static BonusDecision Next(BonusPolicy policy, BonusRunState state)
    {
        if (state.Purchases >= policy.MaxPurchasesPerRun)
        {
            return new(null, $"this run made its {policy.MaxPurchasesPerRun} purchases (max_purchases_per_run)");
        }

        if (!state.WedgeDone && WedgeDue(policy, state) && state.Points >= BonusPurchase.WedgePoints)
        {
            return new(BonusPurchase.Wedge, "a wedge is due");
        }

        var spare = state.Points - policy.KeepPoints;
        if (policy.Vip && !state.VipDone && spare >= BonusPurchase.VipReservePoints)
        {
            return new(BonusPurchase.Vip, "VIP is topped up every run");
        }

        foreach (var gb in policy.UploadAmountsGb.OrderDescending())
        {
            if (spare >= gb * BonusPurchase.PointsPerUploadGb)
            {
                return new(BonusPurchase.Upload(gb), $"the points above keep_points pay for {gb} GB");
            }
        }

        return new(null, NothingReason(policy, state, spare));
    }

    /// <summary>
    /// Every purchase a run would make from <paramref name="state"/>, assuming each goes through at its price; for a dry
    /// run and for showing what the next run would buy.
    /// </summary>
    public static BonusRunPlan PlanRun(BonusPolicy policy, BonusRunState state)
    {
        var purchases = new List<BonusPurchase>();
        while (true)
        {
            var decision = Next(policy, state);
            if (decision.Purchase is not { } purchase)
            {
                return new(purchases, decision.Reason);
            }

            purchases.Add(purchase);
            state = After(state, purchase, state.Points - purchase.Price);
        }
    }

    /// <summary>The run's state after <paramref name="purchase"/> went through and left <paramref name="pointsAfter"/>.</summary>
    public static BonusRunState After(BonusRunState state, BonusPurchase purchase, double pointsAfter) => state with
    {
        Points = pointsAfter,
        Purchases = state.Purchases + 1,
        WedgeDone = state.WedgeDone || purchase.Kind == BonusPurchaseKind.Wedge,
        VipDone = state.VipDone || purchase.Kind == BonusPurchaseKind.Vip,
        LastWedgeAt = purchase.Kind == BonusPurchaseKind.Wedge ? state.Now : state.LastWedgeAt,
    };

    /// <summary>When the next wedge is due; null when wedges aren't bought.</summary>
    public static DateTimeOffset? NextWedgeDue(BonusPolicy policy, DateTimeOffset? lastWedgeAt) =>
        policy.WedgeEveryHours <= 0 ? null
        : lastWedgeAt is { } last ? last + TimeSpan.FromHours(policy.WedgeEveryHours) - WedgeDueSlack
        : DateTimeOffset.MinValue;

    private static bool WedgeDue(BonusPolicy policy, BonusRunState state) =>
        NextWedgeDue(policy, state.LastWedgeAt) is { } due && state.Now >= due;

    private static string NothingReason(BonusPolicy policy, BonusRunState state, double spare)
    {
        var points = Points(state.Points);
        if (!state.WedgeDone && WedgeDue(policy, state))
        {
            return $"a wedge is due, but {points} points are less than the {Points(BonusPurchase.WedgePoints)} it costs";
        }

        if (policy.UploadAmountsGb.Count == 0)
        {
            return state.Purchases == 0 ? "nothing is due" : "nothing more is due";
        }

        var smallest = policy.UploadAmountsGb.Min();
        return spare <= 0
            ? $"the {points} points leave nothing above keep_points {Points(policy.KeepPoints)}"
            : $"the {Points(spare)} points above keep_points {Points(policy.KeepPoints)} don't pay for {smallest} GB "
                + $"({Points(smallest * BonusPurchase.PointsPerUploadGb)})";
    }

    /// <summary>Points as the pages and the log show them: whole, with thousands separators.</summary>
    public static string Points(double points) => Math.Floor(points).ToString("N0", CultureInfo.InvariantCulture);
}
