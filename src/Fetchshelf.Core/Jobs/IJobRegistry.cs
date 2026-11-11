namespace Fetchshelf.Core.Jobs;

/// <summary>
/// Registers named background jobs, tracks their last run, and triggers and cancels them on demand.
/// </summary>
public interface IJobRegistry
{
    /// <summary>Registers a job whose run returns what it did, or null when there is nothing to say.</summary>
    void Register(string name, Func<CancellationToken, Task<JobOutcome?>> runAsync, string? description = null);

    /// <summary>Registers a job that reports nothing beyond how its run ended.</summary>
    void Register(string name, Func<CancellationToken, Task> runAsync, string? description = null);

    IReadOnlyList<RegisteredJob> GetAll();

    RegisteredJob? Get(string name);

    /// <summary>Records when the job is next due to be triggered by its timer. Ignored if the job is not registered.</summary>
    void SetNextScheduledRun(string name, DateTimeOffset? nextRun);

    /// <summary>
    /// Loads the last run of every job from the store, so it shows straight after a restart. Runs the store still has
    /// as running are recorded as interrupted first. Does nothing without a store.
    /// </summary>
    Task LoadLastRunsAsync();

    /// <summary>Runs the job now; does nothing if it is already running. Completes when this run, if any, has finished.</summary>
    Task TriggerAsync(string name, JobTrigger trigger = JobTrigger.Manual, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the job now or, if it is already running, once more as soon as the current run finishes, so work queued
    /// during that run isn't left for the next scheduled one. Requests made during the same run share one extra run.
    /// Completes when the run that starts after this call has finished.
    /// </summary>
    Task TriggerOrQueueAsync(string name, JobTrigger trigger = JobTrigger.Manual, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the job's current run, and drops a run queued after it. Returns false when the job isn't running. The
    /// run is recorded as <see cref="JobRunResult.Cancelled"/> once it has stopped.
    /// </summary>
    bool Cancel(string name);

    /// <summary>Stops every running job because Fetchshelf is shutting down; those runs are recorded as interrupted.</summary>
    void InterruptAll();

    /// <summary>
    /// Completes once every run so far has been written to the store, or given up on. Runs are written in the background,
    /// so a busy database never holds up or fails a job.
    /// </summary>
    Task RunRecordsWrittenAsync();
}
