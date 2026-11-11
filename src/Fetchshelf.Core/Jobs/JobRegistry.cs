using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Core.Jobs;

/// <summary><see cref="IJobRegistry"/> that keeps each job's state in memory and, with a store, its runs in the database.</summary>
public sealed class JobRegistry(ILogger<JobRegistry>? logger = null, TimeProvider? timeProvider = null, IJobRunStore? store = null) : IJobRegistry
{
    /// <summary>The summary of a run cancelled with <see cref="Cancel"/>.</summary>
    public const string CancelledSummary = "Cancelled from the Jobs page";

    /// <summary>The summary of a run Fetchshelf stopped during, whether it was shutting down or found the run unfinished at start.</summary>
    public const string InterruptedSummary = "Fetchshelf stopped during this run";

    private readonly ConcurrentDictionary<string, RegisteredJob> _jobs = new();
    private readonly ConcurrentDictionary<string, JobRunRecord> _loadedRuns = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILogger<JobRegistry> _logger = logger ?? NullLogger<JobRegistry>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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
            LastRun = _loadedRuns.GetValueOrDefault(name),
        };
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
            foreach (var (name, run) in await store.LastRunsAsync())
            {
                _loadedRuns[name] = run;
                if (_jobs.TryGetValue(name, out var job) && job.LastRun is null)
                {
                    job.LastRun = run;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the last job runs from the database: {Error}", ex.Message);
        }
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

        return RunUntilNothingQueuedAsync(job, trigger, cancellationToken);
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

        return RunUntilNothingQueuedAsync(job, trigger, cancellationToken);
    }

    public bool Cancel(string name)
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
            if (cancellation is null)
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

    /// <summary>
    /// Marks the job as running, with the cancellation source of its next run. Called under the job's lock, so the job
    /// is never running without a source <see cref="Cancel"/> can reach, not even between a run and the one queued after it.
    /// </summary>
    private void Claim(RegisteredJob job, CancellationToken cancellationToken)
    {
        job.IsRunning = true;
        job.RunCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        job.CancelRequested = false;
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
        var runId = await RecordAsync(name, () => store!.StartedAsync(name, startedAt, trigger));

        JobRunResult result;
        string? summary;
        IReadOnlyDictionary<string, long>? counts = null;
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
            (result, summary) = (JobRunResult.Failure, ex.Message);
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
        job.LastRun = new JobRunRecord(startedAt, result, summary, finishedAt, trigger, counts);
        if (runId is { } id)
        {
            await RecordAsync(name, async () =>
            {
                await store!.FinishedAsync(id, finishedAt, result, summary, counts);
                return id;
            });
        }
    }

    private double Elapsed(DateTimeOffset startedAt) => (_timeProvider.GetUtcNow() - startedAt).TotalSeconds;

    /// <summary>Writes to the store, if there is one. A failure is logged and returns null: the job runs on regardless.</summary>
    private async Task<long?> RecordAsync(string name, Func<Task<long>> write)
    {
        if (store is null)
        {
            return null;
        }

        try
        {
            return await write();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record a run of job {JobName} in the database: {Error}", name, ex.Message);
            return null;
        }
    }
}
