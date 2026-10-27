using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>Display model for one job row on the Jobs page, Fetchshelf-spec.md §10.1.</summary>
public sealed record JobStatusViewModel(string Name, string? Description, DateTimeOffset? LastStartedAt, string LastResultText, bool IsRunning, DateTimeOffset? NextScheduledRun)
{
    /// <summary>"4m 12s" for a timed job that is idle, else null.</summary>
    public string? NextRunText(DateTimeOffset now)
    {
        if (IsRunning || NextScheduledRun is not { } next)
        {
            return null;
        }

        var remaining = next - now;
        var seconds = (int)Math.Max(0, Math.Ceiling(remaining.TotalSeconds));
        return seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";
    }

    public static JobStatusViewModel From(RegisteredJob job)
    {
        var lastResultText = job.LastRun switch
        {
            null => "Never run",
            { Result: JobRunResult.Success } => "Success",
            { Result: JobRunResult.Failure, ErrorMessage: var error } => $"Error: {error}",
            _ => "Unknown",
        };

        return new JobStatusViewModel(job.Name, job.Description, job.LastRun?.StartedAt, lastResultText, job.IsRunning, job.NextScheduledRun);
    }
}
