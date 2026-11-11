using Fetchshelf.Core.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Fetchshelf.Core.Tests;

/// <summary>What the registry writes to its <see cref="IJobRunStore"/>, and how it cancels a run.</summary>
public class JobRegistryRunRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TriggerAsync_SuccessfulRun_IsRecordedAsStartedAndFinishedWithItsOutcome()
    {
        var store = new InMemoryJobRunStore();
        var registry = new JobRegistry(store: store);
        var counts = new Dictionary<string, long> { ["linked"] = 3 };
        registry.Register("Link", _ => Task.FromResult<JobOutcome?>(new JobOutcome("3 torrents linked", counts)));

        await registry.TriggerAsync("Link", JobTrigger.Schedule);
        await registry.RunRecordsWrittenAsync();

        var run = Assert.Single(store.Runs);
        Assert.Equal(("Link", JobRunResult.Success, "3 torrents linked", JobTrigger.Schedule), (run.JobName, run.Result, run.Summary, run.Trigger));
        Assert.NotNull(run.FinishedAt);
        Assert.Equal(3, run.Counts!["linked"]);
        var lastRun = registry.Get("Link")!.LastRun!;
        Assert.Equal(("3 torrents linked", JobTrigger.Schedule), (lastRun.Summary, lastRun.Trigger));
        Assert.Equal(3, lastRun.Counts!["linked"]);
    }

    [Fact]
    public async Task TriggerAsync_RunCompletesWithErrors_IsRecordedWithTheMessageAndCounts()
    {
        var store = new InMemoryJobRunStore();
        var registry = new JobRegistry(store: store);
        registry.Register("Link", _ => throw new JobCompletedWithErrorsException(
            "2 torrents linked; 1 could not be linked", new Dictionary<string, long> { ["failed"] = 1 }));

        await registry.TriggerAsync("Link");
        await registry.RunRecordsWrittenAsync();

        var run = Assert.Single(store.Runs);
        Assert.Equal((JobRunResult.CompletedWithErrors, "2 torrents linked; 1 could not be linked"), (run.Result, run.Summary));
        Assert.Equal(1, run.Counts!["failed"]);
    }

    [Fact]
    public async Task TriggerAsync_RunFails_IsRecordedWithTheError()
    {
        var store = new InMemoryJobRunStore();
        var registry = new JobRegistry(store: store);
        registry.Register("Link", _ => throw new InvalidOperationException("boom"));

        await registry.TriggerAsync("Link");
        await registry.RunRecordsWrittenAsync();

        Assert.Equal((JobRunResult.Failure, "boom"), (store.Runs[0].Result, store.Runs[0].Summary));
    }

    [Fact]
    public async Task TriggerAsync_StoreThrows_TheJobRunsAndItsLastRunIsKept()
    {
        var registry = new JobRegistry(store: new FailingJobRunStore());
        var ran = false;
        registry.Register("Link", _ =>
        {
            ran = true;
            return Task.FromResult<JobOutcome?>(new JobOutcome("done"));
        });

        await registry.TriggerAsync("Link");

        Assert.True(ran);
        Assert.Equal((JobRunResult.Success, "done"), (registry.Get("Link")!.LastRun!.Result, registry.Get("Link")!.LastRun!.Summary));
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancel_RunningJob_CancelsItsTokenAndIsRecordedAsCancelled()
    {
        var store = new InMemoryJobRunStore();
        var registry = new JobRegistry(store: store);
        var started = new TaskCompletionSource();
        registry.Register("Hardcover", async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        var run = registry.TriggerAsync("Hardcover");
        await started.Task;
        Assert.True(registry.Cancel("Hardcover"));
        await run;
        await registry.RunRecordsWrittenAsync();

        var lastRun = registry.Get("Hardcover")!.LastRun!;
        Assert.Equal((JobRunResult.Cancelled, JobRegistry.CancelledSummary), (lastRun.Result, lastRun.Summary));
        Assert.Equal(JobRunResult.Cancelled, store.Runs[0].Result);
        Assert.False(registry.Get("Hardcover")!.IsRunning);
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancel_RunningJobWithAQueuedRun_DropsTheQueuedRun()
    {
        var registry = new JobRegistry();
        var runs = 0;
        var started = new TaskCompletionSource();
        registry.Register("Download", async ct =>
        {
            runs++;
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        var first = registry.TriggerAsync("Download");
        await started.Task;
        var queued = registry.TriggerOrQueueAsync("Download", JobTrigger.Chained);
        registry.Cancel("Download");
        await Task.WhenAll(first, queued);

        Assert.Equal(1, runs);
        Assert.False(registry.Get("Download")!.IsRunning);
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancel_RunEndingAtTheSameTime_NeverThrows()
    {
        // The run can end, and dispose its cancellation source, between Cancel finding the run and cancelling it.
        var registry = new JobRegistry();
        registry.Register("Link", _ => Task.CompletedTask);
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        using var stop = new CancellationTokenSource();
        var canceller = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    registry.Cancel("Link");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        for (var i = 0; i < 2_000; i++)
        {
            await registry.TriggerAsync("Link");
        }

        await stop.CancelAsync();
        await canceller;
        Assert.Empty(exceptions);
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancel_BetweenARunAndTheRunQueuedAfterIt_DropsTheQueuedRun()
    {
        var logger = new PausingLogger("finished");
        var registry = new JobRegistry(logger);
        var runs = 0;
        // Continued on a thread of its own: the logger holds the run's thread, which must not be the test's.
        var releaseRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register("Download", async _ =>
        {
            runs++;
            await releaseRun.Task;
        });

        var first = registry.TriggerAsync("Download");
        var queued = registry.TriggerOrQueueAsync("Download", JobTrigger.Chained);
        releaseRun.SetResult();
        Assert.True(logger.Paused.Wait(TimeSpan.FromSeconds(5))); // the first run is over, and the queued one not started yet

        Assert.True(registry.Get("Download")!.IsRunning);
        Assert.True(registry.Cancel("Download"));
        logger.Resume.Set();
        await Task.WhenAll(first, queued);

        Assert.Equal(1, runs);
        Assert.False(registry.Get("Download")!.IsRunning);
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerAsync_StoreSlowToWrite_TheJobDoesNotWaitForIt()
    {
        var store = new HeldStore();
        var registry = new JobRegistry(store: store);
        var ran = false;
        registry.Register("Link", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await registry.TriggerAsync("Link"); // while the database is still busy with the run's first write

        Assert.True(ran);
        Assert.False(registry.Get("Link")!.IsRunning);
        store.Release.SetResult();
        await registry.RunRecordsWrittenAsync();
        Assert.Equal(JobRunResult.Success, Assert.Single(store.Inner.Runs).Result);
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerAsync_StoreFailsAFewTimes_TheRunIsStillRecorded()
    {
        var time = new FakeTimeProvider(Now);
        var store = new FlakyStore(failures: 2);
        var registry = new JobRegistry(timeProvider: time, store: store);
        registry.Register("Link", _ => Task.FromResult<JobOutcome?>(new JobOutcome("3 torrents linked")));

        await registry.TriggerAsync("Link");
        await AdvanceUntilAsync(time, registry.RunRecordsWrittenAsync());

        var run = Assert.Single(store.Inner.Runs);
        Assert.Equal((JobRunResult.Success, "3 torrents linked"), (run.Result, run.Summary));
    }

    [Fact(Timeout = 10_000)]
    public async Task TriggerAsync_StartNeverRecorded_TheFinishCreatesTheRun()
    {
        var time = new FakeTimeProvider(Now);
        var store = new FlakyStore(failures: JobRunWriterRetries + 1); // every try at the start fails
        var registry = new JobRegistry(timeProvider: time, store: store);
        registry.Register("Link", _ => Task.FromResult<JobOutcome?>(new JobOutcome("done")));

        await registry.TriggerAsync("Link", JobTrigger.Startup);
        await AdvanceUntilAsync(time, registry.RunRecordsWrittenAsync());

        var run = Assert.Single(store.Inner.Runs);
        Assert.Equal((JobRunResult.Success, "done", JobTrigger.Startup, Now), (run.Result, run.Summary, run.Trigger, run.StartedAt));
    }

    /// <summary>How many times a failed write is tried again before it is given up on.</summary>
    private const int JobRunWriterRetries = 4;

    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Task task)
    {
        while (!task.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(5);
        }

        await task;
    }

    /// <summary>Holds the logging thread at the first message containing <paramref name="text"/> until resumed.</summary>
    private sealed class PausingLogger(string text) : ILogger<JobRegistry>
    {
        private int _paused;

        public ManualResetEventSlim Paused { get; } = new();

        public ManualResetEventSlim Resume { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains(text, StringComparison.Ordinal) && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                Paused.Set();
                Resume.Wait();
            }
        }
    }

    /// <summary>A store whose first write waits until released, like a database another job holds locked.</summary>
    private sealed class HeldStore : IJobRunStore
    {
        public InMemoryJobRunStore Inner { get; } = new();

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger)
        {
            await Release.Task;
            return await Inner.StartedAsync(jobName, startedAt, trigger);
        }

        public Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts) =>
            Inner.FinishedAsync(runId, finishedAt, result, summary, counts);

        public Task InterruptUnfinishedAsync(string summary) => Inner.InterruptUnfinishedAsync(summary);

        public Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync() => Inner.LastRunsAsync();
    }

    /// <summary>A store whose first <c>failures</c> attempts to record a run's start fail, as with "database is locked".</summary>
    private sealed class FlakyStore(int failures) : IJobRunStore
    {
        private int _failuresLeft = failures;

        public InMemoryJobRunStore Inner { get; } = new();

        public Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger) =>
            _failuresLeft-- > 0
                ? throw new InvalidOperationException("database is locked")
                : Inner.StartedAsync(jobName, startedAt, trigger);

        public Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts) =>
            Inner.FinishedAsync(runId, finishedAt, result, summary, counts);

        public Task InterruptUnfinishedAsync(string summary) => Inner.InterruptUnfinishedAsync(summary);

        public Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync() => Inner.LastRunsAsync();
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancel_WithTheNumberOfAnEarlierRun_LeavesTheRunStartedSinceGoing()
    {
        var registry = new JobRegistry();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        registry.Register("Link", async ct =>
        {
            if (++runs == 2)
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        });
        await registry.TriggerAsync("Link");
        var shownRunNumber = registry.Get("Link")!.RunNumber; // the run a page showed a Cancel button for, now over

        var second = registry.TriggerAsync("Link");
        await started.Task;

        Assert.False(registry.Cancel("Link", shownRunNumber));
        Assert.True(registry.Get("Link")!.IsRunning);
        Assert.True(registry.Cancel("Link", registry.Get("Link")!.RunNumber));
        await second;
        Assert.Equal(JobRunResult.Cancelled, registry.Get("Link")!.LastRun!.Result);
    }

    [Fact]
    public async Task RunNumber_CountsEachRun()
    {
        var registry = new JobRegistry();
        registry.Register("Link", _ => Task.CompletedTask);

        await registry.TriggerAsync("Link");
        await registry.TriggerAsync("Link");

        Assert.Equal(2, registry.Get("Link")!.RunNumber);
    }

    [Fact]
    public void Cancel_IdleJob_ReturnsFalse()
    {
        var registry = new JobRegistry();
        registry.Register("Link", _ => Task.CompletedTask);

        Assert.False(registry.Cancel("Link"));
        Assert.False(registry.Cancel("not-registered"));
    }

    [Fact(Timeout = 10_000)]
    public async Task InterruptAll_RunningJob_IsRecordedAsInterrupted()
    {
        var registry = new JobRegistry();
        var started = new TaskCompletionSource();
        registry.Register("Link", async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        var run = registry.TriggerAsync("Link");
        await started.Task;
        registry.InterruptAll();
        await run;

        Assert.Equal(
            (JobRunResult.Interrupted, JobRegistry.InterruptedSummary),
            (registry.Get("Link")!.LastRun!.Result, registry.Get("Link")!.LastRun!.Summary));
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancel_JobWaitingForAJobItStarted_LeavesThatJobRunning()
    {
        var registry = new JobRegistry();
        var cleanStarted = new TaskCompletionSource();
        var releaseClean = new TaskCompletionSource();
        registry.Register("Clean", async _ =>
        {
            cleanStarted.SetResult();
            await releaseClean.Task;
        });
        registry.Register("Link", async ct => await registry.TriggerAsync("Clean", JobTrigger.Chained).WaitAsync(ct));

        var link = registry.TriggerAsync("Link");
        await cleanStarted.Task;
        registry.Cancel("Link");
        await link;

        Assert.Equal(JobRunResult.Cancelled, registry.Get("Link")!.LastRun!.Result);
        Assert.True(registry.Get("Clean")!.IsRunning);
        releaseClean.SetResult();
    }

    [Fact]
    public async Task LoadLastRunsAsync_StoredRuns_ShowAsEachJobsLastRunAndAnUnfinishedOneAsInterrupted()
    {
        var store = new InMemoryJobRunStore();
        var linkId = await store.StartedAsync("Link", Now.AddHours(-2), JobTrigger.Schedule);
        await store.FinishedAsync(linkId, Now.AddHours(-2).AddMinutes(1), JobRunResult.Success, "3 torrents linked", null);
        await store.StartedAsync("Download", Now.AddMinutes(-5), JobTrigger.Chained);
        var registry = new JobRegistry(timeProvider: new FixedTimeProvider(Now), store: store);
        registry.Register("Link", _ => Task.CompletedTask);

        await registry.LoadLastRunsAsync();
        registry.Register("Download", _ => Task.CompletedTask);

        Assert.Equal((JobRunResult.Success, "3 torrents linked"), (registry.Get("Link")!.LastRun!.Result, registry.Get("Link")!.LastRun!.Summary));
        var download = registry.Get("Download")!.LastRun!;
        Assert.Equal((JobRunResult.Interrupted, null, Now.AddMinutes(-5)), (download.Result, download.FinishedAt, download.StartedAt));
        Assert.Contains(registry.GetAll(), j => j.Name == "Download" && j.LastRun is not null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record StoredRun(long Id, string JobName, DateTimeOffset StartedAt, JobTrigger Trigger)
    {
        public JobRunResult Result { get; set; } = JobRunResult.Running;

        public DateTimeOffset? FinishedAt { get; set; }

        public string? Summary { get; set; }

        public IReadOnlyDictionary<string, long>? Counts { get; set; }
    }

    private sealed class InMemoryJobRunStore : IJobRunStore
    {
        public List<StoredRun> Runs { get; } = [];

        public Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger)
        {
            Runs.Add(new StoredRun(Runs.Count + 1, jobName, startedAt, trigger));
            return Task.FromResult((long)Runs.Count);
        }

        public Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts)
        {
            var run = Runs.Single(r => r.Id == runId);
            (run.FinishedAt, run.Result, run.Summary, run.Counts) = (finishedAt, result, summary, counts);
            return Task.CompletedTask;
        }

        public Task InterruptUnfinishedAsync(string summary)
        {
            foreach (var run in Runs.Where(r => r.Result == JobRunResult.Running))
            {
                (run.Result, run.Summary) = (JobRunResult.Interrupted, summary);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, JobRunRecord>>(Runs
                .GroupBy(r => r.JobName)
                .Select(g => g.MaxBy(r => r.Id)!)
                .ToDictionary(r => r.JobName, r => new JobRunRecord(r.StartedAt, r.Result, r.Summary, r.FinishedAt, r.Trigger, r.Counts)));
    }

    private sealed class FailingJobRunStore : IJobRunStore
    {
        public Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger) =>
            throw new InvalidOperationException("database is locked");

        public Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts) =>
            throw new InvalidOperationException("database is locked");

        public Task InterruptUnfinishedAsync(string summary) => throw new InvalidOperationException("database is locked");

        public Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync() => throw new InvalidOperationException("database is locked");
    }
}
