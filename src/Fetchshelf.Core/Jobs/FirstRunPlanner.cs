namespace Fetchshelf.Core.Jobs;

/// <summary>When a timed job first runs after Fetchshelf starts.</summary>
/// <param name="At">When the run is due.</param>
/// <param name="IsStartup">
/// Whether it is the soon-after-start run of a job that was due or had never run, recorded as
/// <see cref="JobTrigger.Startup"/>, rather than the next run of the job's usual rhythm.
/// </param>
public sealed record FirstRun(DateTimeOffset At, bool IsStartup);

public static class FirstRunPlanner
{
    /// <summary>
    /// The scheduler starts each run one <paramref name="interval"/> after the previous one ended, and a restart keeps
    /// that rhythm: a job whose last run ended less than an interval ago next runs one interval after that end. A run
    /// the restart cut off has no known end, so its start counts instead; a restart loop can't make a job run more
    /// often than its interval. A job that has never run, or is overdue, runs
    /// <paramref name="startupDelay"/> after start, which staggers the jobs so they don't all start at once. A last run
    /// that seems to lie ahead, because the clock was set back, counts as having just ended.
    /// </summary>
    public static FirstRun Plan(DateTimeOffset now, JobRunRecord? lastRun, TimeSpan interval, TimeSpan startupDelay)
    {
        if (lastRun is null)
        {
            return new FirstRun(now + startupDelay, IsStartup: true);
        }

        var lastEnd = lastRun.FinishedAt ?? lastRun.StartedAt;
        var due = (lastEnd < now ? lastEnd : now) + interval;
        return due > now ? new FirstRun(due, IsStartup: false) : new FirstRun(now + startupDelay, IsStartup: true);
    }
}
