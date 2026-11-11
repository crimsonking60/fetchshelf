using Fetchshelf.Core.Jobs;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Core.Tests;

/// <summary>The run-finished hook: what <see cref="IJobRunListener"/>s are told, and what a listener can't do.</summary>
public class JobRegistryListenerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private sealed class Recorder : IJobRunListener
    {
        public List<JobRunFinished> Runs { get; } = [];

        public void RunFinished(JobRunFinished finished) => Runs.Add(finished);
    }

    private sealed class Throwing : IJobRunListener
    {
        public void RunFinished(JobRunFinished finished) => throw new InvalidOperationException("listener broke");
    }

    private sealed class RestoredStore(string jobName, params JobRunRecord[] runs) : IJobRunStore
    {
        public Task<long> StartedAsync(string name, DateTimeOffset startedAt, JobTrigger trigger) => Task.FromResult(1L);

        public Task FinishedAsync(long runId, JobRunRecord run) => Task.CompletedTask;

        public Task InterruptUnfinishedAsync(string summary) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, IReadOnlyList<JobRunRecord>>> RecentRunsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<JobRunRecord>>>(
                new Dictionary<string, IReadOnlyList<JobRunRecord>> { [jobName] = runs });
    }

    [Fact]
    public async Task TriggerAsync_FirstRun_IsReportedWithItsNameAndNoPreviousRun()
    {
        var recorder = new Recorder();
        var registry = new JobRegistry(listeners: [recorder]);
        registry.Register("Link", _ => Task.FromResult<JobOutcome?>(new JobOutcome("3 torrents linked")));

        await registry.TriggerAsync("Link", JobTrigger.Schedule);

        var finished = Assert.Single(recorder.Runs);
        Assert.Equal("Link", finished.JobName);
        Assert.Equal((JobRunResult.Success, "3 torrents linked", JobTrigger.Schedule), (finished.Run.Result, finished.Run.Summary, finished.Run.Trigger));
        Assert.Null(finished.PreviousRun);
        Assert.Null(finished.FailingBefore);
        Assert.Null(finished.FailingAfter);
    }

    [Fact]
    public async Task TriggerAsync_FailureAfterSuccessAndSuccessAfterFailure_ReportTheFailingStateBeforeAndAfter()
    {
        var recorder = new Recorder();
        var registry = new JobRegistry(listeners: [recorder]);
        var fail = false;
        registry.Register("Link", _ => fail ? throw new InvalidOperationException("boom") : Task.CompletedTask);

        await registry.TriggerAsync("Link");
        fail = true;
        await registry.TriggerAsync("Link");
        await registry.TriggerAsync("Link");
        fail = false;
        await registry.TriggerAsync("Link");

        Assert.Equal([false, true, false, false], recorder.Runs.Select(r => r.StartedFailing));
        Assert.Equal([false, false, false, true], recorder.Runs.Select(r => r.Recovered));
        Assert.Equal(JobRunResult.Success, recorder.Runs[1].PreviousRun!.Result);
        Assert.Equal(2, recorder.Runs[3].FailingBefore!.FailedRuns);
        Assert.Null(recorder.Runs[3].FailingAfter);
        Assert.Equal(("boom", 2), (recorder.Runs[2].FailingAfter!.LatestError, recorder.Runs[2].FailingAfter!.FailedRuns));
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancel_AndInterruptAll_AreReportedToo()
    {
        var recorder = new Recorder();
        var registry = new JobRegistry(listeners: [recorder]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Register("Link", async ct =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        var first = registry.TriggerAsync("Link");
        await started.Task;
        registry.Cancel("Link");
        await first;
        started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = registry.TriggerAsync("Link");
        await started.Task;
        registry.InterruptAll();
        await second;

        Assert.Equal([JobRunResult.Cancelled, JobRunResult.Interrupted], recorder.Runs.Select(r => r.Run.Result));
    }

    [Fact]
    public async Task TriggerAsync_ListenerThrows_IsLoggedTheNextListenerIsStillToldAndTheRunIsRecorded()
    {
        var recorder = new Recorder();
        var logger = new CollectingLogger();
        var registry = new JobRegistry(logger, listeners: [new Throwing(), recorder]);
        registry.Register("Link", _ => Task.FromResult<JobOutcome?>(new JobOutcome("done")));

        await registry.TriggerAsync("Link");

        Assert.Single(recorder.Runs);
        Assert.Equal((JobRunResult.Success, "done"), (registry.Get("Link")!.LastRun!.Result, registry.Get("Link")!.LastRun!.Summary));
        Assert.Contains(logger.Warnings, w => w.Contains("listener broke", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadLastRunsAsync_ReportsNothing_AndTheFirstRunAfterItGetsTheRestoredRunAndFailingState()
    {
        var failed = new JobRunRecord(Now.AddHours(-1), JobRunResult.Failure, "qBittorrent down", Now.AddHours(-1), FailureKind: JobFailureKind.Other);
        var recorder = new Recorder();
        var registry = new JobRegistry(store: new RestoredStore("Link", failed), listeners: [recorder]);
        registry.Register("Link", _ => Task.CompletedTask);

        await registry.LoadLastRunsAsync();
        Assert.Empty(recorder.Runs);

        await registry.TriggerAsync("Link");

        var finished = Assert.Single(recorder.Runs);
        Assert.Equal(failed, finished.PreviousRun);
        Assert.Equal(new JobFailure(Now.AddHours(-1), 1, "qBittorrent down", JobFailureKind.Other), finished.FailingBefore);
        Assert.True(finished.Recovered);
    }

    [Fact]
    public async Task LoadLastRunsAsync_JobFailingBeforeTheRestart_IsNotNewlyFailingWhenItFailsAgain()
    {
        var failed = new JobRunRecord(Now.AddHours(-1), JobRunResult.Failure, "qBittorrent down", Now.AddHours(-1));
        var recorder = new Recorder();
        var registry = new JobRegistry(store: new RestoredStore("Link", failed), listeners: [recorder]);
        registry.Register("Link", _ => throw new InvalidOperationException("qBittorrent down"));
        await registry.LoadLastRunsAsync();

        await registry.TriggerAsync("Link");

        Assert.False(recorder.Runs.Single().StartedFailing);
        Assert.Equal(2, recorder.Runs.Single().FailingAfter!.FailedRuns);
    }

    private sealed class CollectingLogger : ILogger<JobRegistry>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add($"{formatter(state, exception)} {exception?.Message}");
            }
        }
    }
}
