namespace Fetchshelf.Core.Jobs;

/// <summary>Keeps job runs beyond a restart. The registry calls it; a store that fails never fails a job.</summary>
public interface IJobRunStore
{
    /// <summary>Records a run as started and returns its id, for <see cref="FinishedAsync"/>.</summary>
    Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger);

    /// <summary>Records how a run ended, from <paramref name="run"/>, and drops that job's oldest runs beyond the ones kept.</summary>
    Task FinishedAsync(long runId, JobRunRecord run);

    /// <summary>
    /// Marks every run still recorded as running as interrupted: Fetchshelf stopped before it ended. Its finish time
    /// stays empty, since when it stopped is not known; the time of the restart would count the downtime as run time.
    /// </summary>
    Task InterruptUnfinishedAsync(string summary);

    /// <summary>The runs kept of every job that has any, by job name, oldest first.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<JobRunRecord>>> RecentRunsAsync();
}
