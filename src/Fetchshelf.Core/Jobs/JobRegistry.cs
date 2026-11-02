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

    public void Register(string name, Func<CancellationToken, Task> runAsync, string? description = null) =>
        _jobs[name] = new RegisteredJob { Name = name, RunAsync = runAsync, Description = description };

    public IReadOnlyList<RegisteredJob> GetAll() => _jobs.Values.OrderBy(j => j.Name, StringComparer.Ordinal).ToList();

    public RegisteredJob? Get(string name) => _jobs.GetValueOrDefault(name);

    public void SetNextScheduledRun(string name, DateTimeOffset? nextRun)
    {
        if (_jobs.TryGetValue(name, out var job))
        {
            job.NextScheduledRun = nextRun;
        }
    }

    public async Task TriggerAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(name, out var job))
        {
            throw new InvalidOperationException($"No job named '{name}' is registered.");
        }

        if (job.IsRunning)
        {
            return;
        }

        var startedAt = _timeProvider.GetUtcNow();
        job.IsRunning = true;
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
        finally
        {
            job.IsRunning = false;
        }
    }
}
