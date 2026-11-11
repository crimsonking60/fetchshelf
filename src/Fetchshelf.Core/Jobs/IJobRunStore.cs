namespace Fetchshelf.Core.Jobs;

/// <summary>Keeps job runs beyond a restart. The registry calls it; a store that fails never fails a job.</summary>
public interface IJobRunStore
{
    /// <summary>Records a run as started and returns its id, for <see cref="FinishedAsync"/>.</summary>
    Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger);

    /// <summary>Records how a run ended, and drops that job's oldest runs beyond the ones kept.</summary>
    Task FinishedAsync(long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts);

    /// <summary>Marks every run still recorded as running as interrupted at <paramref name="at"/>: Fetchshelf stopped before it ended.</summary>
    Task InterruptUnfinishedAsync(DateTimeOffset at, string summary);

    /// <summary>The newest run of every job that has one, by job name.</summary>
    Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync();
}
