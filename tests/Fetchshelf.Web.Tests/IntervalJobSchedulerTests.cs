using Fetchshelf.Core.Jobs;
using Fetchshelf.Web.Jobs;
using Microsoft.Extensions.Time.Testing;

namespace Fetchshelf.Web.Tests;

public class IntervalJobSchedulerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    [Fact(Timeout = 10_000)]
    public async Task NeverRun_FirstRunsAfterItsStartupDelayThenEveryInterval()
    {
        var time = new TimerCountingTimeProvider(Start);
        var registry = new JobRegistry(timeProvider: time);
        var runs = new RunRecorder(time);
        registry.Register("Link", runs.RunAsync);
        using var scheduler = new IntervalJobScheduler(registry, "Link", Interval, StartupDelay, time);

        await scheduler.StartAsync(CancellationToken.None);
        await time.WaitForTimersAsync(1); // ExecuteAsync runs on a thread of its own
        Assert.Equal(Start + StartupDelay, registry.Get("Link")!.NextScheduledRun);

        time.Advance(StartupDelay - TimeSpan.FromSeconds(1));
        Assert.Empty(runs.Times);
        time.Advance(TimeSpan.FromSeconds(1));
        await runs.WaitForRunAsync(1);
        await WaitUntilAsync(() => registry.Get("Link")!.LastRun is not null);
        Assert.Equal(JobTrigger.Startup, registry.Get("Link")!.LastRun!.Trigger);

        await time.WaitForTimersAsync(2);
        Assert.Equal(Start + StartupDelay + Interval, registry.Get("Link")!.NextScheduledRun);
        time.Advance(Interval);
        await runs.WaitForRunAsync(2);

        Assert.Equal([Start + StartupDelay, Start + StartupDelay + Interval], runs.Times);
        await WaitUntilAsync(() => registry.Get("Link")!.LastRun!.Trigger == JobTrigger.Schedule);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 10_000)]
    public async Task RanRecently_FirstRunsOneIntervalAfterTheLastRunEnded()
    {
        var time = new TimerCountingTimeProvider(Start);
        var store = new OneRunStore("Link", new JobRunRecord(Start.AddMinutes(-10), JobRunResult.Success, null, Start.AddMinutes(-9)));
        var registry = new JobRegistry(timeProvider: time, store: store);
        var runs = new RunRecorder(time);
        registry.Register("Link", runs.RunAsync);
        await registry.LoadLastRunsAsync();
        using var scheduler = new IntervalJobScheduler(registry, "Link", Interval, StartupDelay, time);

        await scheduler.StartAsync(CancellationToken.None);
        await time.WaitForTimersAsync(1);
        Assert.Equal(Start.AddMinutes(21), registry.Get("Link")!.NextScheduledRun);

        time.Advance(TimeSpan.FromMinutes(21));
        await runs.WaitForRunAsync(1);

        Assert.Equal([Start.AddMinutes(21)], runs.Times);
        await scheduler.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        while (!condition())
        {
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Counts the timers the scheduler creates, so the test moves the clock only once the scheduler waits on it: moved
    /// earlier, a delay would be measured from the new time and never end.
    /// </summary>
    private sealed class TimerCountingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _timers;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _timers);
            return timer;
        }

        public Task WaitForTimersAsync(int count) => WaitUntilAsync(() => Volatile.Read(ref _timers) >= count);
    }

    private sealed class RunRecorder(TimeProvider time)
    {
        public List<DateTimeOffset> Times { get; } = [];

        public Task RunAsync(CancellationToken cancellationToken)
        {
            lock (Times)
            {
                Times.Add(time.GetUtcNow());
            }

            return Task.CompletedTask;
        }

        public async Task WaitForRunAsync(int count) => await WaitUntilAsync(() =>
        {
            lock (Times)
            {
                return Times.Count >= count;
            }
        });
    }

    private sealed class OneRunStore(string jobName, JobRunRecord run) : IJobRunStore
    {
        public Task<long> StartedAsync(string name, DateTimeOffset startedAt, JobTrigger trigger) => Task.FromResult(1L);

        public Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts) =>
            Task.CompletedTask;

        public Task InterruptUnfinishedAsync(string summary) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, JobRunRecord>>(new Dictionary<string, JobRunRecord> { [jobName] = run });
    }
}
