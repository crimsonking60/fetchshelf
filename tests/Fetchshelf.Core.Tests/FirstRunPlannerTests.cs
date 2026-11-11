using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Core.Tests;

public class FirstRunPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Stagger = TimeSpan.FromSeconds(90);

    private static JobRunRecord Run(DateTimeOffset startedAt, DateTimeOffset? finishedAt, JobRunResult result = JobRunResult.Success) =>
        new(startedAt, result, null, finishedAt);

    [Fact]
    public void Plan_RanRecentlyAndNotYetDue_RunsOneIntervalAfterTheLastRunEnded() =>
        // A 25-minute run that ended 10 minutes ago: due in 20 minutes, as if there had been no restart.
        Assert.Equal(
            new FirstRun(Now.AddMinutes(20), IsStartup: false),
            FirstRunPlanner.Plan(Now, Run(Now.AddMinutes(-35), Now.AddMinutes(-10)), Interval, Stagger));

    [Fact]
    public void Plan_NeverRun_RunsAfterItsStartupDelay() =>
        Assert.Equal(new FirstRun(Now + Stagger, IsStartup: true), FirstRunPlanner.Plan(Now, null, Interval, Stagger));

    [Fact]
    public void Plan_Overdue_RunsAfterItsStartupDelay() =>
        Assert.Equal(
            new FirstRun(Now + Stagger, IsStartup: true),
            FirstRunPlanner.Plan(Now, Run(Now.AddHours(-3), Now.AddHours(-3).AddMinutes(1)), Interval, Stagger));

    [Fact]
    public void Plan_StartedWithinAnIntervalAndCutOffByTheRestart_CountsFromItsStart() =>
        // The run has no known end; its start counts, so a restart loop can't run a job more often than its interval.
        Assert.Equal(
            new FirstRun(Now.AddMinutes(29), IsStartup: false),
            FirstRunPlanner.Plan(Now, Run(Now.AddMinutes(-1), null, JobRunResult.Interrupted), Interval, Stagger));

    [Fact]
    public void Plan_LastRunAheadOfTheClock_RunsOneIntervalFromNow() =>
        // The clock was set back a day: the last run seems to lie in the future, and must not hold the job back a day.
        Assert.Equal(
            new FirstRun(Now + Interval, IsStartup: false),
            FirstRunPlanner.Plan(Now, Run(Now.AddDays(1), Now.AddDays(1).AddMinutes(2)), Interval, Stagger));
}
