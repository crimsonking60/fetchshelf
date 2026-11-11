using System.Globalization;

namespace Fetchshelf.Core.Notifications;

/// <summary>What MAM's answers mean for notifications: a refusal, a rate limit, a hit-and-run, a spent budget.</summary>
public static class AccountNotificationRules
{
    /// <summary>MAM refused `mam_id` (HTTP 403); calls are paused until <paramref name="pausedUntil"/>.</summary>
    public static Notification MamRefused(DateTimeOffset at, DateTimeOffset pausedUntil) => new(
        NotificationEvent.MamRefused,
        "MAM refused mam_id",
        $"MAM refused mam_id (HTTP 403). MAM jobs are paused until {Hours(pausedUntil)}. Replace mam_id and restart Fetchshelf.",
        at,
        new Dictionary<string, object?> { ["http_status"] = 403, ["paused_until"] = pausedUntil },
        "/Account");

    /// <summary>MAM blocked the account (HTTP 406). Nothing is paused: every call fails until MAM lifts the block.</summary>
    public static Notification MamBlocked(DateTimeOffset at) => new(
        NotificationEvent.MamRefused,
        "MAM blocked the account",
        "MAM blocked the account (HTTP 406). MAM jobs fail until MAM lifts the block.",
        at,
        new Dictionary<string, object?> { ["http_status"] = 406 },
        "/Account");

    /// <summary>MAM asked Fetchshelf to slow down (HTTP 429); calls wait a minute.</summary>
    public static Notification RateLimited(DateTimeOffset at) => new(
        NotificationEvent.RateLimited,
        "MAM rate limit",
        "MAM asked Fetchshelf to slow down (HTTP 429); MAM calls are paused for a minute.",
        at,
        new Dictionary<string, object?> { ["http_status"] = 429 },
        "/Account");

    /// <summary>The key the last hit-and-run count is stored under.</summary>
    public const string HitAndRunStateKey = "hit_and_run.inactHnr";

    /// <summary>The key of MAM's snatch summary entry for "Not Seeding - H&amp;R - Not Yet Satisfied".</summary>
    public const string HitAndRunSnatchKey = "inactHnr";

    /// <summary>
    /// Compares a read count with the stored one: a higher count is sent and stored; a lower one is stored without
    /// sending, so a later rise is noticed; the same count does neither. With nothing stored, the stored count counts
    /// as 0, so an existing hit-and-run is sent once.
    /// </summary>
    /// <returns>Whether to send, and the count to store (null to leave what is stored).</returns>
    public static (bool Send, long? Store) DecideHitAndRun(long? stored, long current)
    {
        var known = stored ?? 0;
        return current > known ? (true, current) : current < known ? (false, current) : (false, null);
    }

    public static Notification HitAndRun(long count, DateTimeOffset at)
    {
        var plural = count != 1;
        return new Notification(
            NotificationEvent.HitAndRun,
            "New hit-and-run on MAM",
            $"MAM counts {count.ToString(CultureInfo.InvariantCulture)} {(plural ? "hit-and-runs" : "hit-and-run")} "
            + $"(Not Seeding - H&R - Not Yet Satisfied). Seed {(plural ? "them" : "it")} again to avoid a penalty.",
            at,
            new Dictionary<string, object?> { ["count"] = count },
            "/Account");
    }

    /// <summary>
    /// Selections left queued for lack of budget: sent when a run leaves some after one that left none, not on every run
    /// while they wait.
    /// </summary>
    /// <param name="leftQueued">How many selections this run left queued because a budget is spent.</param>
    /// <param name="limitedBy">Which budget and what it comes to, such as "the ratio budget is 0 B (ratio 2.01, min_ratio 2)".</param>
    /// <param name="previousLeftQueued">How many the Download job's previous run left; 0 for its first.</param>
    public static Notification? BudgetSpent(int leftQueued, string? limitedBy, long previousLeftQueued, DateTimeOffset at)
    {
        if (leftQueued <= 0 || previousLeftQueued > 0)
        {
            return null;
        }

        return new Notification(
            NotificationEvent.BudgetSpent,
            "Selections are waiting",
            $"{leftQueued} {(leftQueued == 1 ? "selection waits" : "selections wait")}: {limitedBy ?? "a budget is spent"}.",
            at,
            new Dictionary<string, object?> { ["left_queued"] = leftQueued, ["reason"] = limitedBy },
            "/Selected");
    }

    private static string Hours(DateTimeOffset time) => time.ToUniversalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
}
