using System.Collections.Concurrent;

namespace Fetchshelf.Core.Jobs;

/// <summary>In-memory <see cref="IJobRegistry"/></summary>
public sealed class JobRegistry : IJobRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredJob> _jobs = new();

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

        var startedAt = DateTimeOffset.UtcNow;
        job.IsRunning = true;
        try
        {
            await job.RunAsync(cancellationToken);
            job.LastRun = new JobRunRecord(startedAt, JobRunResult.Success, null);
        }
        catch (Exception ex)
        {
            job.LastRun = new JobRunRecord(startedAt, JobRunResult.Failure, ex.Message);
        }
        finally
        {
            job.IsRunning = false;
        }
    }
}
