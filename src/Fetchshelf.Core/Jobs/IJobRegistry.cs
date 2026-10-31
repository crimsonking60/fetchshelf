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

    Task TriggerAsync(string name, CancellationToken cancellationToken = default);
}
