using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Core.Jobs;

/// <summary>In-memory <see cref="IJobRegistry"/></summary>
public sealed class JobRegistry(ILogger<JobRegistry>? logger = null, TimeProvider? timeProvider = null) : IJobRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredJob> _jobs = new();
    private readonly ILogger<JobRegistry> _logger = logger ?? NullLogger<JobRegistry>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Registers a job under <paramref name="name"/>. A name can only be registered once: replacing a job would leave its
    /// schedulers triggering the other one, which is exactly the silent mix-up unique names in the configuration
    /// prevent.
    /// </summary>
    public void Register(string name, Func<CancellationToken, Task> runAsync, string? description = null)
    {
        if (!_jobs.TryAdd(name, new RegisteredJob { Name = name, RunAsync = runAsync, Description = description }))
        {
            throw new InvalidOperationException($"A job named '{name}' is already registered.");
        }
    }

    public IReadOnlyList<RegisteredJob> GetAll() => _jobs.Values.OrderBy(j => j.Name, StringComparer.Ordinal).ToList();

    public RegisteredJob? Get(string name) => _jobs.GetValueOrDefault(name);

    public void SetNextScheduledRun(string name, DateTimeOffset? nextRun)
    {
        if (_jobs.TryGetValue(name, out var job))
        {
            job.NextScheduledRun = nextRun;
        }
    }

    public Task TriggerAsync(string name, CancellationToken cancellationToken = default)
    {
        var job = Find(name);
        lock (job.Gate)
        {
            if (job.IsRunning)
            {
                return Task.CompletedTask;
            }

            job.IsRunning = true;
        }

        return RunUntilNothingQueuedAsync(job, cancellationToken);
    }

    public Task TriggerOrQueueAsync(string name, CancellationToken cancellationToken = default)
    {
        var job = Find(name);
        lock (job.Gate)
        {
            if (job.IsRunning)
            {
                job.QueuedRun ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _logger.LogInformation("Job {JobName} is running; it runs once more when this run ends", name);
                return job.QueuedRun.Task;
            }

            job.IsRunning = true;
        }

        return RunUntilNothingQueuedAsync(job, cancellationToken);
    }

    private RegisteredJob Find(string name) =>
        _jobs.TryGetValue(name, out var job) ? job : throw new InvalidOperationException($"No job named '{name}' is registered.");

    /// <summary>Runs the job, then again for as long as runs were queued meanwhile; clears <see cref="RegisteredJob.IsRunning"/> at the end.</summary>
    private async Task RunUntilNothingQueuedAsync(RegisteredJob job, CancellationToken cancellationToken)
    {
        TaskCompletionSource? finishing = null;
        while (true)
        {
            try
            {
                await RunOnceAsync(job, cancellationToken);
            }
            finally
            {
                finishing?.TrySetResult();
            }

            lock (job.Gate)
            {
                finishing = job.QueuedRun;
                job.QueuedRun = null;
                if (finishing is null)
                {
                    job.IsRunning = false;
                    return;
                }
            }
        }
    }

    private async Task RunOnceAsync(RegisteredJob job, CancellationToken cancellationToken)
    {
        var name = job.Name;
        var startedAt = _timeProvider.GetUtcNow();
        _logger.LogInformation("Job {JobName} started", name);
        try
        {
            await job.RunAsync(cancellationToken);
            job.LastRun = new JobRunRecord(startedAt, JobRunResult.Success, null);
            _logger.LogInformation("Job {JobName} finished in {Elapsed:0.0}s", name, (_timeProvider.GetUtcNow() - startedAt).TotalSeconds);
        }
        catch (JobCompletedWithErrorsException ex)
        {
            job.LastRun = new JobRunRecord(startedAt, JobRunResult.CompletedWithErrors, ex.Message);
            _logger.LogWarning(
                "Job {JobName} finished in {Elapsed:0.0}s with errors: {Error}", name, (_timeProvider.GetUtcNow() - startedAt).TotalSeconds, ex.Message);
        }
        catch (Exception ex)
        {
            job.LastRun = new JobRunRecord(startedAt, JobRunResult.Failure, ex.Message);
            _logger.LogWarning(ex, "Job {JobName} failed: {Error}", name, ex.Message);
        }
    }
}
