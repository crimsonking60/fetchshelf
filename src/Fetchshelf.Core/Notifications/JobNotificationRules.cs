using System.Globalization;
using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Core.Notifications;

/// <summary>
/// What a finished job run means for notifications: a job that starts failing, one that works again, a run that finishes
/// with errors the one before didn't. Only changes are sent, not states: a job that fails every 10 minutes for a day
/// sends one failure and one recovery.
/// </summary>
public static class JobNotificationRules
{
    /// <summary>The notification for a run that ended, or null when it changes nothing.</summary>
    public static Notification? From(JobRunFinished finished)
    {
        var (name, run) = (finished.JobName, finished.Run);
        var at = run.FinishedAt ?? run.StartedAt;
        var link = $"/Jobs/History?job={Uri.EscapeDataString(name)}";

        if (finished.StartedFailing && finished.FailingAfter is { } failing)
        {
            return new Notification(
                NotificationEvent.JobFailing,
                $"Job {name} is failing",
                $"Job {name} failed: {failing.LatestError}",
                at,
                new Dictionary<string, object?> { ["job"] = name, ["error"] = failing.LatestError, ["since"] = failing.Since },
                link,
                SupersededBy: Superseding(failing.LatestKind));
        }

        if (finished.Recovered && finished.FailingBefore is { } before)
        {
            var runs = before.FailedRuns == 1 ? "1 failed run" : $"{before.FailedRuns} failed runs";
            return new Notification(
                NotificationEvent.JobRecovered,
                $"Job {name} works again",
                $"Job {name} works again, after {runs} since {FormatTime(before.Since, at)}.",
                at,
                new Dictionary<string, object?> { ["job"] = name, ["failed_runs"] = before.FailedRuns, ["since"] = before.Since },
                link,
                SupersededBy: Superseding(before.LatestKind));
        }

        if (run.Result == JobRunResult.CompletedWithErrors && finished.PreviousRun?.Result != JobRunResult.CompletedWithErrors)
        {
            return new Notification(
                NotificationEvent.JobErrors,
                $"{name} finished with errors",
                $"{name}: {run.Summary}",
                at,
                new Dictionary<string, object?> { ["job"] = name, ["summary"] = run.Summary, ["counts"] = run.Counts },
                "/Errors");
        }

        return null;
    }

    /// <summary>
    /// The MAM event that tells the same story as a job failing for this reason: every MAM job fails when MAM refuses
    /// `mam_id`, and one message about it beats one per job.
    /// </summary>
    private static NotificationEvent? Superseding(JobFailureKind kind) => kind switch
    {
        JobFailureKind.MamRefused or JobFailureKind.MamBlocked => NotificationEvent.MamRefused,
        JobFailureKind.MamRateLimited => NotificationEvent.RateLimited,
        _ => null,
    };

    /// <summary>"08:30 UTC", or with the date when it wasn't on the same day as <paramref name="now"/>.</summary>
    public static string FormatTime(DateTimeOffset time, DateTimeOffset now)
    {
        var (utc, today) = (time.ToUniversalTime(), now.ToUniversalTime());
        return utc.Date == today.Date
            ? utc.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }
}

/// <summary>Tells the <see cref="INotificationSink"/> what job runs mean, as they end.</summary>
public sealed class JobRunNotifier(INotificationSink sink) : IJobRunListener
{
    public void RunFinished(JobRunFinished finished)
    {
        if (JobNotificationRules.From(finished) is { } notification)
        {
            sink.Publish(notification);
        }
    }
}
