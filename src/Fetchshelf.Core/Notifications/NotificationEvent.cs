namespace Fetchshelf.Core.Notifications;

/// <summary>What a notification is about. The names the configuration uses are in <see cref="NotificationEvents"/>.</summary>
public enum NotificationEvent
{
    /// <summary>MAM refused `mam_id` (HTTP 403) or blocked the account (HTTP 406).</summary>
    MamRefused,

    /// <summary>MAM asked Fetchshelf to slow down (HTTP 429).</summary>
    RateLimited,

    /// <summary>A job started failing.</summary>
    JobFailing,

    /// <summary>A job that was failing works again.</summary>
    JobRecovered,

    /// <summary>A run finished with errors after one that didn't.</summary>
    JobErrors,

    /// <summary>A torrent was sent to qBittorrent.</summary>
    Grabbed,

    /// <summary>A torrent was linked into a library.</summary>
    Linked,

    /// <summary>`mark_removed` found that MAM no longer has a stored torrent.</summary>
    RemovedFromMam,

    /// <summary>MAM counts more hit-and-runs than the last count seen.</summary>
    HitAndRun,

    /// <summary>The Download job left selections queued because the ratio budget or the unsatisfied-snatch slots are spent.</summary>
    BudgetSpent,
}

/// <summary>The configuration's names for <see cref="NotificationEvent"/>, and what kind of event each is.</summary>
public static class NotificationEvents
{
    /// <summary>The name that selects every event in a `[[notify]]` block's `events`.</summary>
    public const string AllName = "all";

    private static readonly Dictionary<NotificationEvent, string> Names = new()
    {
        [NotificationEvent.MamRefused] = "mam_refused",
        [NotificationEvent.RateLimited] = "rate_limited",
        [NotificationEvent.JobFailing] = "job_failing",
        [NotificationEvent.JobRecovered] = "job_recovered",
        [NotificationEvent.JobErrors] = "job_errors",
        [NotificationEvent.Grabbed] = "grabbed",
        [NotificationEvent.Linked] = "linked",
        [NotificationEvent.RemovedFromMam] = "removed_from_mam",
        [NotificationEvent.HitAndRun] = "hit_and_run",
        [NotificationEvent.BudgetSpent] = "budget_spent",
    };

    /// <summary>Every event, in the order the documentation lists them.</summary>
    public static IReadOnlyList<NotificationEvent> All { get; } = [.. Names.Keys];

    /// <summary>The configuration's name for the event, such as <c>job_failing</c>.</summary>
    public static string Name(NotificationEvent notificationEvent) => Names[notificationEvent];

    /// <summary>The event with this configuration name; false for an unknown one. The name <see cref="AllName"/> isn't an event.</summary>
    public static bool TryParse(string name, out NotificationEvent notificationEvent)
    {
        foreach (var (candidate, candidateName) in Names)
        {
            if (string.Equals(candidateName, name, StringComparison.Ordinal))
            {
                notificationEvent = candidate;
                return true;
            }
        }

        notificationEvent = default;
        return false;
    }

    /// <summary>The names of every event, for messages that say what is accepted.</summary>
    public static string NameList => string.Join(", ", Names.Values);

    /// <summary>Whether the event says something is wrong, which gets a warning tag, a red colour and a higher priority.</summary>
    public static bool IsProblem(NotificationEvent notificationEvent) => notificationEvent is
        NotificationEvent.MamRefused or NotificationEvent.RateLimited or NotificationEvent.JobFailing
        or NotificationEvent.JobErrors or NotificationEvent.HitAndRun;

    /// <summary>Whether the event stops work until someone acts, which asks for the highest priority.</summary>
    public static bool IsUrgent(NotificationEvent notificationEvent) =>
        notificationEvent is NotificationEvent.MamRefused or NotificationEvent.JobFailing;
}
