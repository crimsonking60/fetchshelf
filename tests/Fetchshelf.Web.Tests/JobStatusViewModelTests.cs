using Fetchshelf.Core.Jobs;
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

    [Fact]
    public async Task From_RunCompletedWithErrors_ShowsFinishedWithErrors()
    {
        var registry = new JobRegistry();
        registry.Register("Link", _ => throw new JobCompletedWithErrorsException("1 torrent could not be linked; see the Errors page."));
        await registry.TriggerAsync("Link");

        Assert.Equal(
            "Finished with errors: 1 torrent could not be linked; see the Errors page.",
            JobStatusViewModel.From(registry.Get("Link")!).LastResultText);
    }

    [Theory]
    [InlineData(JobRunResult.Success, "3 torrents linked", "Success: 3 torrents linked")]
    [InlineData(JobRunResult.Success, null, "Success")]
    [InlineData(JobRunResult.Failure, "boom", "Error: boom")]
    [InlineData(JobRunResult.Cancelled, JobRegistry.CancelledSummary, "Cancelled")]
    [InlineData(JobRunResult.Interrupted, JobRegistry.InterruptedSummary, "Interrupted: Fetchshelf stopped during this run")]
    public void ResultText_ShowsTheResultAndSummary(JobRunResult result, string? summary, string expected) =>
        Assert.Equal(expected, JobStatusViewModel.ResultText(new JobRunRecord(Now, result, summary)));

    [Fact]
    public void RunningText_RunningJob_ShowsHowLongItHasRun() =>
        Assert.Equal("Running for 2m 10s", (Job(null, isRunning: true) with { RunningSince = Now.AddSeconds(-130) }).RunningText(Now));

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(130, "2m 10s")]
    [InlineData(3900, "1h 5m")]
    public void Duration_ReadsInTheLargestUsefulUnits(int seconds, string expected) =>
        Assert.Equal(expected, JobRunLabels.Duration(TimeSpan.FromSeconds(seconds)));
}
