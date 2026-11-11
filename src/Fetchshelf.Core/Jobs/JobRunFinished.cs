namespace Fetchshelf.Core.Jobs;

/// <summary>A job run that has just ended, with what a listener needs to tell what changed since the run before.</summary>
/// <param name="JobName">The job's registered name.</param>
/// <param name="Run">The run that just ended, with any result: success, finished with errors, failure, cancelled, interrupted.</param>
/// <param name="PreviousRun">
/// The job's last run before this one that ran to its end (<see cref="RegisteredJob.LastRunToTheEnd"/>), so a cancelled or
/// interrupted run in between doesn't make this one's result look new; null when there is none.
/// </param>
/// <param name="FailingBefore">Whether, and since when, the job was failing before this run.</param>
/// <param name="FailingAfter">Whether, and since when, it is failing after it.</param>
public sealed record JobRunFinished(
    string JobName,
    JobRunRecord Run,
    JobRunRecord? PreviousRun,
    JobFailure? FailingBefore,
    JobFailure? FailingAfter)
{
    /// <summary>The job has started failing: this run failed after runs that didn't.</summary>
    public bool StartedFailing => FailingBefore is null && FailingAfter is not null;

    /// <summary>The job has stopped failing: this run did its work (it succeeded or finished with errors) after failing ones.</summary>
    public bool Recovered => FailingBefore is not null && FailingAfter is null
        && Run.Result is JobRunResult.Success or JobRunResult.CompletedWithErrors;
}
