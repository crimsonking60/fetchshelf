using Fetchshelf.Web.Pages.Jobs;

namespace Fetchshelf.Web.Tests;

public class JobStatusViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static JobStatusViewModel Job(DateTimeOffset? next, bool isRunning = false) =>
        new("Link", null, null, "Never run", isRunning, next);

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(60, "1m 0s")]
    [InlineData(252, "4m 12s")]
    [InlineData(-3, "0s")]
    public void NextRunText_TimedIdleJob_ShowsRemainingTime(int secondsAhead, string expected) =>
        Assert.Equal(expected, Job(Now.AddSeconds(secondsAhead)).NextRunText(Now));

    [Fact]
    public void NextRunText_NoTimer_IsNull() => Assert.Null(Job(null).NextRunText(Now));

    [Fact]
    public void NextRunText_JobIsRunning_IsNull() =>
        Assert.Null(Job(Now.AddSeconds(30), isRunning: true).NextRunText(Now));
}
