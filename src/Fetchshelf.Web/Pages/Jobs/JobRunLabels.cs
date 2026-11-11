using System.Globalization;
using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>How a job run's result, trigger, duration and counts read on the Jobs pages.</summary>
public static class JobRunLabels
{
    public static string Result(JobRunResult result) => result switch
    {
        JobRunResult.Success => "Success",
        JobRunResult.Failure => "Error",
        JobRunResult.CompletedWithErrors => "Finished with errors",
        JobRunResult.Running => "Running",
        JobRunResult.Cancelled => "Cancelled",
        JobRunResult.Interrupted => "Interrupted",
        _ => "Unknown",
    };

    public static string Trigger(JobTrigger trigger) => trigger switch
    {
        JobTrigger.Schedule => "Schedule",
        JobTrigger.Startup => "Startup",
        JobTrigger.Manual => "Manual",
        JobTrigger.Chained => "Another job",
        _ => "Unknown",
    };

    /// <summary>"45s", "2m 10s" or "1h 5m".</summary>
    public static string Duration(TimeSpan duration)
    {
        var seconds = (long)Math.Max(0, Math.Floor(duration.TotalSeconds));
        return seconds switch
        {
            < 60 => $"{seconds}s",
            < 3600 => $"{seconds / 60}m {seconds % 60}s",
            _ => $"{seconds / 3600}h {seconds % 3600 / 60}m",
        };
    }

    /// <summary>"linked: 3, failed: 1", or null without counts.</summary>
    public static string? Counts(IReadOnlyDictionary<string, long>? counts) =>
        counts is null || counts.Count == 0
            ? null
            : string.Join(", ", counts.Select(pair => $"{pair.Key.Replace('_', ' ')}: {pair.Value.ToString(CultureInfo.InvariantCulture)}"));
}
