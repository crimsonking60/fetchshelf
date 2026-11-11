using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Core.Tests;

public class FirstRunPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Stagger = TimeSpan.FromSeconds(90);

    [Fact]
    public void Plan_RanRecentlyAndNotYetDue_RunsOneIntervalAfterTheLastStart() =>
        Assert.Equal(new FirstRun(Now.AddMinutes(20), IsStartup: false), FirstRunPlanner.Plan(Now, Now.AddMinutes(-10), Interval, Stagger));

    [Fact]
    public void Plan_NeverRun_RunsAfterItsStartupDelay() =>
        Assert.Equal(new FirstRun(Now + Stagger, IsStartup: true), FirstRunPlanner.Plan(Now, null, Interval, Stagger));

    [Fact]
    public void Plan_Overdue_RunsAfterItsStartupDelay() =>
        Assert.Equal(new FirstRun(Now + Stagger, IsStartup: true), FirstRunPlanner.Plan(Now, Now.AddHours(-3), Interval, Stagger));

    [Fact]
    public void Plan_StartedWithinAnIntervalAndInterrupted_WaitsForItsInterval() =>
        // The last start counts however the run ended, so a restart loop can't run a job more often than its interval.
        Assert.Equal(new FirstRun(Now.AddMinutes(29), IsStartup: false), FirstRunPlanner.Plan(Now, Now.AddMinutes(-1), Interval, Stagger));
}
