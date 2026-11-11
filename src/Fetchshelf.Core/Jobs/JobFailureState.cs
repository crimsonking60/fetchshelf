namespace Fetchshelf.Core.Jobs;

/// <summary>A job whose runs keep failing: since when, how many runs in a row, and how the latest one failed.</summary>
/// <param name="Since">When the first failed run after the last one that didn't fail started.</param>
/// <param name="FailedRuns">How many runs have failed since.</param>
/// <param name="LatestError">The error of the latest failed run.</param>
/// <param name="LatestKind">What kind of error that was.</param>
public sealed record JobFailure(DateTimeOffset Since, int FailedRuns, string? LatestError, JobFailureKind LatestKind);

/// <summary>Whether a job is failing, worked out from its runs.</summary>
public static class JobFailureState
{
    /// <summary>
    /// The state after one more run. A failed run starts or extends the streak. A run that succeeded or finished with
    /// errors (it did its work) ends it. A run that was cancelled, that Fetchshelf stopped during, or that is still
    /// running says nothing either way and leaves it as it was: ending it there would drop the job from the failing
    /// ones without a recovery, and a failure after it would count as a new streak.
    /// </summary>
    public static JobFailure? Next(JobFailure? current, JobRunRecord run) => run.Result switch
    {
        JobRunResult.Failure => new JobFailure(
            current?.Since ?? run.StartedAt, (current?.FailedRuns ?? 0) + 1, run.Summary, run.FailureKind ?? JobFailureKind.Other),
        JobRunResult.Success or JobRunResult.CompletedWithErrors => null,
        _ => current,
    };

    /// <summary>The state after a job's runs, oldest first: null when the job isn't failing.</summary>
    public static JobFailure? From(IEnumerable<JobRunRecord> runsOldestFirst) => runsOldestFirst.Aggregate((JobFailure?)null, Next);
}
