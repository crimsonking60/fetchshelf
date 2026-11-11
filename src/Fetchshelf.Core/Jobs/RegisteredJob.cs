namespace Fetchshelf.Core.Jobs;

/// <summary>A background job registered by name, with its most recent run record.</summary>
public sealed class RegisteredJob
{
    public required string Name { get; init; }

    public required Func<CancellationToken, Task<JobOutcome?>> RunAsync { get; init; }

    /// <summary>Brief, user-facing description of what this job does. Null if none was given.</summary>
    public string? Description { get; init; }

    /// <summary>The last run that ended, or that a restart interrupted. Null if the job has never run.</summary>
    public JobRunRecord? LastRun { get; internal set; }

    /// <summary>
    /// The last run that ran to its end: it succeeded, finished with errors or failed. Runs that were cancelled or that
    /// Fetchshelf stopped during are left out. Null if there is none.
    /// </summary>
    internal JobRunRecord? LastRunToTheEnd { get; set; }

    /// <summary>Since when the job's runs keep failing; null while it isn't failing. See <see cref="JobFailureState"/>.</summary>
    public JobFailure? Failing { get; internal set; }

    /// <summary>When the interval scheduler will next trigger this job. Null if the job has no timer.</summary>
    public DateTimeOffset? NextScheduledRun { get; internal set; }

    /// <summary>Whether this job's <see cref="RunAsync"/> is currently executing.</summary>
    public bool IsRunning { get; internal set; }

    /// <summary>
    /// Numbers the job's runs since Fetchshelf started; while the job runs, the number of the current run. Lets a Cancel
    /// from a page shown a moment ago stop only the run that page showed.
    /// </summary>
    public long RunNumber { get; internal set; }

    /// <summary>When the current run started; null while the job is idle.</summary>
    public DateTimeOffset? RunningSince { get; internal set; }

    /// <summary>
    /// Guards <see cref="IsRunning"/>, <see cref="QueuedRun"/> and <see cref="RunCancellation"/>, so two triggers can't
    /// both start a run.
    /// </summary>
    internal object Gate { get; } = new();

    /// <summary>Set while a run is wanted after the current one; completes when that run has finished.</summary>
    internal TaskCompletionSource? QueuedRun { get; set; }

    /// <summary>What asked for <see cref="QueuedRun"/> first.</summary>
    internal JobTrigger QueuedTrigger { get; set; }

    /// <summary>Cancels the current run; null while the job is idle.</summary>
    internal CancellationTokenSource? RunCancellation { get; set; }

    /// <summary>Whether the current run was cancelled with <see cref="IJobRegistry.Cancel"/>, rather than by a shutdown.</summary>
    internal bool CancelRequested { get; set; }
}
