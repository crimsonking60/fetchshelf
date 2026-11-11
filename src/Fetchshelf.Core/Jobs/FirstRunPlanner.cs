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
    /// A job that started less than one <paramref name="interval"/> ago, however that run ended, keeps its rhythm and
    /// next runs one interval after that start; a restart loop can't make it run more often. A job that has never run,
    /// or is overdue, runs <paramref name="startupDelay"/> after start, which staggers the jobs so they don't all
    /// start at once.
    /// </summary>
    public static FirstRun Plan(DateTimeOffset now, DateTimeOffset? lastStartedAt, TimeSpan interval, TimeSpan startupDelay) =>
        lastStartedAt is { } last && last + interval > now
            ? new FirstRun(last + interval, IsStartup: false)
            : new FirstRun(now + startupDelay, IsStartup: true);
}
