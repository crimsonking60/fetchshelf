namespace Fetchshelf.Core.Jobs;

/// <summary>
/// Registers named background jobs, tracks their last run, and triggers them on demand.
/// </summary>
public interface IJobRegistry
{
    void Register(string name, Func<CancellationToken, Task> runAsync, string? description = null);

    IReadOnlyList<RegisteredJob> GetAll();

    RegisteredJob? Get(string name);

    /// <summary>Records when the job is next due to be triggered by its timer. Ignored if the job is not registered.</summary>
    void SetNextScheduledRun(string name, DateTimeOffset? nextRun);

    /// <summary>Runs the job now; does nothing if it is already running. Completes when this run, if any, has finished.</summary>
    Task TriggerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the job now or, if it is already running, once more as soon as the current run finishes, so work queued
    /// during that run isn't left for the next scheduled one. Requests made during the same run share one extra run.
    /// Completes when the run that starts after this call has finished.
    /// </summary>
    Task TriggerOrQueueAsync(string name, CancellationToken cancellationToken = default);
}
