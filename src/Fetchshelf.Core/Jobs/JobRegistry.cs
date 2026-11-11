using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Core.Jobs;

/// <summary><see cref="IJobRegistry"/> that keeps each job's state in memory and, with a store, its runs in the database.</summary>
/// <param name="logger">Logs each run.</param>
/// <param name="timeProvider">The clock; the system clock if null.</param>
/// <param name="store">Keeps the runs beyond a restart; without one, they are only kept in memory.</param>
/// <param name="classifyFailure">
/// Says what kind of error ended a failed run, such as a MAM refusal; every failure is <see cref="JobFailureKind.Other"/>
/// without one. Passed in because the exceptions belong to the clients, which this project doesn't know.
/// </param>
/// <param name="listeners">
/// Told of every run that ends, in turn. Given here and not added later, so the set can't change while jobs run. Runs
/// restored from the store at startup are not reported: they happened before this start.
/// </param>
public sealed class JobRegistry(
    ILogger<JobRegistry>? logger = null,
    TimeProvider? timeProvider = null,
    IJobRunStore? store = null,
    Func<Exception, JobFailureKind>? classifyFailure = null,
    IEnumerable<IJobRunListener>? listeners = null) : IJobRegistry
{
    private readonly IJobRunListener[] _listeners = listeners?.ToArray() ?? [];

    /// <summary>The summary of a run cancelled with <see cref="Cancel"/>.</summary>
    public const string CancelledSummary = "Cancelled from the Jobs page";

    /// <summary>The summary of a run Fetchshelf stopped during, whether it was shutting down or found the run unfinished at start.</summary>
    public const string InterruptedSummary = "Fetchshelf stopped during this run";

    private readonly ConcurrentDictionary<string, RegisteredJob> _jobs = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<JobRunRecord>> _loadedRuns = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Task, byte> _runsGoing = new();
    private readonly ILogger<JobRegistry> _logger = logger ?? NullLogger<JobRegistry>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly JobRunWriter? _writer = store is null
        ? null
        : new JobRunWriter(store, logger ?? (ILogger)NullLogger<JobRegistry>.Instance, timeProvider ?? TimeProvider.System);

    /// <summary>
    /// Registers a job under <paramref name="name"/>. A name can only be registered once: replacing a job would leave its
    /// schedulers triggering the other one, which is exactly the silent mix-up unique names in the configuration
    /// prevent.
    /// </summary>
    public void Register(string name, Func<CancellationToken, Task<JobOutcome?>> runAsync, string? description = null)
    {
        var job = new RegisteredJob
        {
            Name = name,
            RunAsync = runAsync,
            Description = description,
        };
        if (_loadedRuns.TryGetValue(name, out var runs))
        {
            Restore(job, runs);
        }

        if (!_jobs.TryAdd(name, job))
        {
            throw new InvalidOperationException($"A job named '{name}' is already registered.");
        }
    }

    public void Register(string name, Func<CancellationToken, Task> runAsync, string? description = null) =>
        Register(name, async ct =>
        {
            await runAsync(ct);
            return (JobOutcome?)null;
        }, description);

    public IReadOnlyList<RegisteredJob> GetAll() => _jobs.Values.OrderBy(j => j.Name, StringComparer.Ordinal).ToList();

    public RegisteredJob? Get(string name) => _jobs.GetValueOrDefault(name);

    public void SetNextScheduledRun(string name, DateTimeOffset? nextRun)
    {
        if (_jobs.TryGetValue(name, out var job))
        {
            job.NextScheduledRun = nextRun;
        }
    }

    public async Task LoadLastRunsAsync()
    {
        if (store is null)
        {
            return;
        }

        try
        {
            await store.InterruptUnfinishedAsync(InterruptedSummary);
            foreach (var (name, runs) in await store.RecentRunsAsync())
            {
                _loadedRuns[name] = runs;
                if (_jobs.TryGetValue(name, out var job) && job.LastRun is null)
                {
                    Restore(job, runs);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the last job runs from the database: {Error}", ex.Message);
        }
    }

    /// <summary>A job's last run and failing state, from its runs kept before the restart, oldest first.</summary>
    private static void Restore(RegisteredJob job, IReadOnlyList<JobRunRecord> runs)
    {
        job.LastRun = runs.Count == 0 ? null : runs[^1];
        job.LastRunToTheEnd = runs.LastOrDefault(RanToTheEnd);
        job.Failing = JobFailureState.From(runs);
    }

    public Task TriggerAsync(string name, JobTrigger trigger = JobTrigger.Manual, CancellationToken cancellationToken = default)
    {
        var job = Find(name);
        lock (job.Gate)
        {
            if (job.IsRunning)
            {
                return Task.CompletedTask;
            }

            Claim(job, cancellationToken);
        }

        return Track(RunUntilNothingQueuedAsync(job, trigger, cancellationToken));
    }

    public Task TriggerOrQueueAsync(string name, JobTrigger trigger = JobTrigger.Manual, CancellationToken cancellationToken = default)
    {
        var job = Find(name);
        lock (job.Gate)
        {
            if (job.IsRunning)
            {
                if (job.QueuedRun is null)
                {
                    job.QueuedRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    job.QueuedTrigger = trigger;
                }

                _logger.LogInformation("Job {JobName} is running; it runs once more when this run ends", name);
                return job.QueuedRun.Task;
            }

            Claim(job, cancellationToken);
        }

        return Track(RunUntilNothingQueuedAsync(job, trigger, cancellationToken));
    }

    public bool Cancel(string name, long? runNumber = null)
    {
        if (!_jobs.TryGetValue(name, out var job))
        {
            return false;
        }

        CancellationTokenSource? cancellation;
        TaskCompletionSource? dropped;
        lock (job.Gate)
        {
            cancellation = job.RunCancellation;
            if (cancellation is null || (runNumber is { } expected && expected != job.RunNumber))
            {
                return false;
            }

            job.CancelRequested = true;
            dropped = job.QueuedRun;
            job.QueuedRun = null;
        }

        _logger.LogInformation("Job {JobName} is being cancelled", name);
        // Whoever waits for the dropped run, such as an autograb that asked for a Download run, carries on as if it ran.
        dropped?.TrySetResult();
        // Cancelled outside the lock, since cancelling can run the job's own code on this thread. The run may end in
        // the meantime and dispose its source: then there is nothing left to stop.
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    public void InterruptAll() => _stopping.Cancel();

    public Task RunsEndedAsync() =>
        // A run that fails has already been recorded as failed: only its end matters here.
        Task.WhenAll(_runsGoing.Keys).ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    public Task RunRecordsWrittenAsync() => _writer?.FlushAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Marks the job as running, with the cancellation source of its next run. Called under the job's lock, so the job
    /// is never running without a source <see cref="Cancel"/> can reach, not even between a run and the one queued after it.
    /// </summary>
    private void Claim(RegisteredJob job, CancellationToken cancellationToken)
    {
        job.IsRunning = true;
        job.RunNumber++;
        job.RunCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        job.CancelRequested = false;
    }

    /// <summary>Keeps the runs going, for <see cref="RunsEndedAsync"/>, until they end.</summary>
    private Task Track(Task runs)
    {
        _runsGoing.TryAdd(runs, 0);
        runs.ContinueWith(ended => _runsGoing.TryRemove(ended, out _), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return runs;
    }

    private RegisteredJob Find(string name) =>
        _jobs.TryGetValue(name, out var job) ? job : throw new InvalidOperationException($"No job named '{name}' is registered.");

    /// <summary>Runs the job, then again for as long as runs were queued meanwhile; clears <see cref="RegisteredJob.IsRunning"/> at the end.</summary>
    private async Task RunUntilNothingQueuedAsync(RegisteredJob job, JobTrigger trigger, CancellationToken cancellationToken)
    {
        TaskCompletionSource? finishing = null;
        while (true)
        {
            CancellationTokenSource cancellation;
            lock (job.Gate)
            {
                cancellation = job.RunCancellation!;
            }

            try
            {
                await RunOnceAsync(job, trigger, cancellation);
            }
            finally
            {
                finishing?.TrySetResult();
            }

            // The finished run's source stays reachable until this point, so a Cancel up to here still drops the
            // queued run, and the queued run gets a fresh source in the same step.
            lock (job.Gate)
            {
                finishing = job.QueuedRun;
                job.QueuedRun = null;
                trigger = job.QueuedTrigger;
                if (finishing is null)
                {
                    job.IsRunning = false;
                    job.RunCancellation = null;
                }
                else
                {
                    Claim(job, cancellationToken);
                }
            }

            cancellation.Dispose();
            if (finishing is null)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(RegisteredJob job, JobTrigger trigger, CancellationTokenSource cancellation)
    {
        var name = job.Name;
        var startedAt = _timeProvider.GetUtcNow();
        lock (job.Gate)
        {
            job.RunningSince = startedAt;
        }

        _logger.LogInformation("Job {JobName} started", name);
        var row = _writer?.Started(name, startedAt, trigger);

        JobRunResult result;
        string? summary;
        IReadOnlyDictionary<string, long>? counts = null;
        JobFailureKind? failureKind = null;
        try
        {
            var outcome = await job.RunAsync(cancellation.Token);
            (result, summary, counts) = (JobRunResult.Success, outcome?.Summary, outcome?.Counts);
            _logger.LogInformation("Job {JobName} finished in {Elapsed:0.0}s", name, Elapsed(startedAt));
        }
        catch (JobCompletedWithErrorsException ex)
        {
            (result, summary, counts) = (JobRunResult.CompletedWithErrors, ex.Message, ex.Counts);
            _logger.LogWarning("Job {JobName} finished in {Elapsed:0.0}s with errors: {Error}", name, Elapsed(startedAt), ex.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            bool cancelRequested;
            lock (job.Gate)
            {
                cancelRequested = job.CancelRequested;
            }

            (result, summary) = cancelRequested
                ? (JobRunResult.Cancelled, CancelledSummary)
                : (JobRunResult.Interrupted, InterruptedSummary);
            _logger.LogInformation("Job {JobName} stopped after {Elapsed:0.0}s: {Reason}", name, Elapsed(startedAt), summary);
        }
        catch (Exception ex)
        {
            (result, summary, failureKind) = (JobRunResult.Failure, ex.Message, classifyFailure?.Invoke(ex) ?? JobFailureKind.Other);
            _logger.LogWarning(ex, "Job {JobName} failed: {Error}", name, ex.Message);
        }
        finally
        {
            lock (job.Gate)
            {
                job.RunningSince = null;
            }
        }

        var finishedAt = _timeProvider.GetUtcNow();
        var run = new JobRunRecord(startedAt, result, summary, finishedAt, trigger, counts, failureKind);
        var (previousRun, failingBefore) = (job.LastRunToTheEnd, job.Failing);
        job.LastRun = run;
        if (RanToTheEnd(run))
        {
            job.LastRunToTheEnd = run;
        }

        job.Failing = JobFailureState.Next(failingBefore, run);
        if (row is not null)
        {
            _writer!.Finished(row, name, run);
        }

        Notify(new JobRunFinished(name, run, previousRun, failingBefore, job.Failing));
    }

    private static bool RanToTheEnd(JobRunRecord run) =>
        run.Result is JobRunResult.Success or JobRunResult.CompletedWithErrors or JobRunResult.Failure;

    /// <summary>Tells every listener; one that throws is logged, and the others are still told.</summary>
    private void Notify(JobRunFinished finished)
    {
        foreach (var listener in _listeners)
        {
            try
            {
                listener.RunFinished(finished);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A listener for the end of job {JobName} failed: {Error}", finished.JobName, ex.Message);
            }
        }
    }

    private double Elapsed(DateTimeOffset startedAt) => (_timeProvider.GetUtcNow() - startedAt).TotalSeconds;
}
