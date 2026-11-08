using Fetchshelf.Core.Jobs;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Core.Tests;

public class JobRegistryTests
{
    [Fact]
    public void Register_JobName_IsDiscoverableAfterward()
    {
        var registry = new JobRegistry();

        registry.Register("example-job", _ => Task.CompletedTask);

        Assert.NotNull(registry.Get("example-job"));
        Assert.Contains(registry.GetAll(), j => j.Name == "example-job");
    }

    [Fact]
    public async Task TriggerAsync_RegisteredJob_RecordsTheStartTimeFromTheTimeProvider()
    {
        var startedAt = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
        var registry = new JobRegistry(timeProvider: new FixedTimeProvider(startedAt));
        registry.Register("example-job", _ => Task.CompletedTask);

        await registry.TriggerAsync("example-job");

        Assert.Equal(startedAt, registry.Get("example-job")!.LastRun!.StartedAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Register_WithDescription_IsStoredOnTheJob()
    {
        var registry = new JobRegistry();

        registry.Register("example-job", _ => Task.CompletedTask, "Does the example thing.");

        Assert.Equal("Does the example thing.", registry.Get("example-job")!.Description);
    }

    [Fact]
    public void SetNextScheduledRun_RegisteredJob_IsStoredOnTheJob()
    {
        var registry = new JobRegistry();
        registry.Register("example-job", _ => Task.CompletedTask);
        var nextRun = DateTimeOffset.UtcNow.AddMinutes(5);

        registry.SetNextScheduledRun("example-job", nextRun);

        Assert.Equal(nextRun, registry.Get("example-job")!.NextScheduledRun);
    }

    [Fact]
    public void SetNextScheduledRun_UnregisteredJob_IsIgnored()
    {
        var registry = new JobRegistry();

        registry.SetNextScheduledRun("missing-job", DateTimeOffset.UtcNow);

        Assert.Null(registry.Get("missing-job"));
    }

    [Fact]
    public void Register_WithoutDescription_IsNull()
    {
        var registry = new JobRegistry();

        registry.Register("example-job", _ => Task.CompletedTask);

        Assert.Null(registry.Get("example-job")!.Description);
    }

    [Fact]
    public async Task TriggerAsync_SuccessfulRun_RecordsStartTimeAndSuccess()
    {
        var registry = new JobRegistry();
        registry.Register("example-job", _ => Task.CompletedTask);
        var before = DateTimeOffset.UtcNow;

        await registry.TriggerAsync("example-job");

        var job = registry.Get("example-job")!;
        Assert.NotNull(job.LastRun);
        Assert.Equal(JobRunResult.Success, job.LastRun!.Result);
        Assert.True(job.LastRun.StartedAt >= before);
        Assert.Null(job.LastRun.ErrorMessage);
    }

    [Fact]
    public async Task TriggerAsync_RunFunctionThrows_RecordsFailureAndJobRemainsRunnable()
    {
        var registry = new JobRegistry();
        registry.Register("failing-job", _ => throw new InvalidOperationException("boom"));

        await registry.TriggerAsync("failing-job");

        var job = registry.Get("failing-job")!;
        Assert.Equal(JobRunResult.Failure, job.LastRun!.Result);
        Assert.Equal("boom", job.LastRun.ErrorMessage);

        // Job remains registered and runnable again after a failure.
        Assert.NotNull(registry.Get("failing-job"));
        await registry.TriggerAsync("failing-job");
        Assert.Equal(JobRunResult.Failure, registry.Get("failing-job")!.LastRun!.Result);
    }

    [Fact]
    public async Task TriggerAsync_OnDemand_RunsJobAndUpdatesLastRun()
    {
        var registry = new JobRegistry();
        var ran = false;
        registry.Register("on-demand-job", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await registry.TriggerAsync("on-demand-job");

        Assert.True(ran);
        Assert.NotNull(registry.Get("on-demand-job")!.LastRun);
    }

    [Fact]
    public async Task TriggerAsync_UnknownJobName_Throws()
    {
        var registry = new JobRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.TriggerAsync("does-not-exist"));
    }

    [Fact]
    public async Task Get_ImmediatelyAfterTrigger_ReflectsUpdatedStatus()
    {
        var registry = new JobRegistry();
        registry.Register("example-job", _ => Task.CompletedTask);

        await registry.TriggerAsync("example-job");
        var statusImmediatelyAfter = registry.Get("example-job")!.LastRun;

        Assert.NotNull(statusImmediatelyAfter);
        Assert.Equal(JobRunResult.Success, statusImmediatelyAfter!.Result);
    }

    [Fact]
    public async Task TriggerAsync_SuccessfulRun_LogsStartAndFinish()
    {
        var logger = new CapturingLogger();
        var registry = new JobRegistry(logger);
        registry.Register("example-job", _ => Task.CompletedTask);

        await registry.TriggerAsync("example-job");

        Assert.Collection(
            logger.Entries,
            entry => Assert.Equal((LogLevel.Information, "Job example-job started"), entry),
            entry =>
            {
                Assert.Equal(LogLevel.Information, entry.Level);
                Assert.StartsWith("Job example-job finished in ", entry.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task TriggerAsync_RunFunctionThrows_LogsWarningWithTheError()
    {
        var logger = new CapturingLogger();
        var registry = new JobRegistry(logger);
        registry.Register("failing-job", _ => throw new InvalidOperationException("boom"));

        await registry.TriggerAsync("failing-job");

        Assert.Equal((LogLevel.Warning, "Job failing-job failed: boom"), logger.Entries[^1]);
    }

    [Fact]
    public async Task TriggerAsync_RunCompletesWithErrors_RecordsCompletedWithErrorsAndLogsWarning()
    {
        var logger = new CapturingLogger();
        var registry = new JobRegistry(logger);
        registry.Register("link", _ => throw new JobCompletedWithErrorsException("2 torrents could not be linked"));

        await registry.TriggerAsync("link");

        var lastRun = registry.Get("link")!.LastRun!;
        Assert.Equal(JobRunResult.CompletedWithErrors, lastRun.Result);
        Assert.Equal("2 torrents could not be linked", lastRun.ErrorMessage);
        var entry = logger.Entries[^1];
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("Job link finished in ", entry.Message, StringComparison.Ordinal);
        Assert.EndsWith(" with errors: 2 torrents could not be linked", entry.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<JobRegistry>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A job whose runs each wait until released, counting how many started.</summary>
    private sealed class GatedJob
    {
        private readonly List<TaskCompletionSource> _releases = [];
        private readonly object _lock = new();

        public int Runs { get; private set; }

        public Task RunAsync(CancellationToken _)
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                Runs++;
                _releases.Add(release);
            }

            return release.Task;
        }

        /// <summary>Lets the run in progress finish.</summary>
        public void ReleaseCurrent()
        {
            lock (_lock)
            {
                _releases[^1].TrySetResult();
            }
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerAsync_WhileRunning_DoesNotRunAgain()
    {
        var registry = new JobRegistry();
        var job = new GatedJob();
        registry.Register("download", job.RunAsync);

        var first = registry.TriggerAsync("download");
        await registry.TriggerAsync("download");
        job.ReleaseCurrent();
        await first;

        Assert.Equal(1, job.Runs);
        Assert.False(registry.Get("download")!.IsRunning);
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerOrQueueAsync_WhileRunning_RunsOnceMoreAfterTheCurrentRun()
    {
        var registry = new JobRegistry();
        var job = new GatedJob();
        registry.Register("download", job.RunAsync);

        var first = registry.TriggerAsync("download");
        var queued = registry.TriggerOrQueueAsync("download");
        Assert.False(queued.IsCompleted);

        job.ReleaseCurrent();
        await WaitUntil(() => job.Runs == 2);
        Assert.False(queued.IsCompleted);

        job.ReleaseCurrent();
        await queued;
        await first;
        Assert.Equal(2, job.Runs);
        Assert.False(registry.Get("download")!.IsRunning);
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerOrQueueAsync_SeveralRequestsDuringOneRun_ShareOneExtraRun()
    {
        var registry = new JobRegistry();
        var job = new GatedJob();
        registry.Register("download", job.RunAsync);

        var first = registry.TriggerAsync("download");
        var queuedA = registry.TriggerOrQueueAsync("download");
        var queuedB = registry.TriggerOrQueueAsync("download");
        job.ReleaseCurrent();
        await WaitUntil(() => job.Runs == 2);
        job.ReleaseCurrent();
        await Task.WhenAll(first, queuedA, queuedB);

        Assert.Equal(2, job.Runs);
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerOrQueueAsync_WhenIdle_RunsNow()
    {
        var registry = new JobRegistry();
        var runs = 0;
        registry.Register("download", _ =>
        {
            runs++;
            return Task.CompletedTask;
        });

        await registry.TriggerOrQueueAsync("download");

        Assert.Equal(1, runs);
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerAsync_ManyAtOnce_StartsOneRun()
    {
        var registry = new JobRegistry();
        var job = new GatedJob();
        registry.Register("download", job.RunAsync);

        // Every call is made, from many threads at once, before the run is released: a call arriving after the run
        // ended would rightly start a new one.
        var triggers = new System.Collections.Concurrent.ConcurrentBag<Task>();
        Parallel.For(0, 20, _ => triggers.Add(registry.TriggerAsync("download")));
        job.ReleaseCurrent();
        await Task.WhenAll(triggers);

        Assert.Equal(1, job.Runs);
    }
}
