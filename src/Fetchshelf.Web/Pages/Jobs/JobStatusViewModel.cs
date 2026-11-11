using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Web.Pages.Jobs;

/// <summary>Display model for one job row on the Jobs page.</summary>
public sealed record JobStatusViewModel(string Name, string? Description, DateTimeOffset? LastStartedAt, string LastResultText, bool IsRunning, DateTimeOffset? NextScheduledRun)
{
    /// <summary>The number of the current run, for its Cancel button. See <see cref="RegisteredJob.RunNumber"/>.</summary>
    public long RunNumber { get; init; }

    /// <summary>When the current run started; null while idle.</summary>
    public DateTimeOffset? RunningSince { get; init; }

    /// <summary>How long the last run took; null if it never finished.</summary>
    public TimeSpan? LastDuration { get; init; }

    /// <summary>The last run's counts, shown on hover over its result.</summary>
    public string? CountsText { get; init; }

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

    /// <summary>"Running for 2m 10s" while the job runs, else null.</summary>
    public string? RunningText(DateTimeOffset now) =>
        !IsRunning ? null
        : RunningSince is { } since ? $"Running for {JobRunLabels.Duration(now - since)}"
        : "Running";

    /// <summary>"Success: 3 torrents linked", or the result alone when the run said nothing more.</summary>
    public static string ResultText(JobRunRecord? run) => run switch
    {
        null => "Never run",
        { Result: JobRunResult.Cancelled } => JobRunLabels.Result(JobRunResult.Cancelled),
        { Summary: { Length: > 0 } summary } => $"{JobRunLabels.Result(run.Result)}: {summary}",
        _ => JobRunLabels.Result(run.Result),
    };

    public static JobStatusViewModel From(RegisteredJob job) =>
        new(job.Name, job.Description, job.LastRun?.StartedAt, ResultText(job.LastRun), job.IsRunning, job.NextScheduledRun)
        {
            RunNumber = job.RunNumber,
            RunningSince = job.RunningSince,
            LastDuration = job.LastRun is { FinishedAt: { } finishedAt } run ? finishedAt - run.StartedAt : null,
            CountsText = JobRunLabels.Counts(job.LastRun?.Counts),
        };
}
