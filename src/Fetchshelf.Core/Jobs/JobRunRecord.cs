namespace Fetchshelf.Core.Jobs;

/// <summary>How a job run ended. Stored as a number, so new values go at the end.</summary>
public enum JobRunResult
{
    Success,
    Failure,

    /// <summary>The run went through to the end, but some of its items failed. See <see cref="JobCompletedWithErrorsException"/>.</summary>
    CompletedWithErrors,

    /// <summary>The run has started and not finished yet.</summary>
    Running,

    /// <summary>The run was stopped with <see cref="IJobRegistry.Cancel"/>.</summary>
    Cancelled,

    /// <summary>Fetchshelf stopped during the run, so it never finished.</summary>
    Interrupted,
}

/// <summary>What started a job run.</summary>
public enum JobTrigger
{
    /// <summary>The job's interval timer.</summary>
    Schedule,

    /// <summary>The job's first run soon after Fetchshelf started, because it was due or had never run.</summary>
    Startup,

    /// <summary>A button in the web UI.</summary>
    Manual,

    /// <summary>Another job, such as the Link job starting a Clean run.</summary>
    Chained,
}

/// <param name="StartedAt">When the run started.</param>
/// <param name="Result">How the run ended.</param>
/// <param name="Summary">One line on what the run did, or the error that ended it. Null if there is nothing to say.</param>
/// <param name="FinishedAt">When the run ended; null while it runs, and for a run whose end isn't known.</param>
/// <param name="Trigger">What started the run.</param>
/// <param name="Counts">The numbers behind <paramref name="Summary"/>, by name, such as <c>linked</c> and <c>failed</c>.</param>
/// <param name="FailureKind">What kind of error ended a <see cref="JobRunResult.Failure"/>; null for any other result.</param>
public sealed record JobRunRecord(
    DateTimeOffset StartedAt,
    JobRunResult Result,
    string? Summary,
    DateTimeOffset? FinishedAt = null,
    JobTrigger Trigger = JobTrigger.Manual,
    IReadOnlyDictionary<string, long>? Counts = null,
    JobFailureKind? FailureKind = null);

/// <summary>
/// What kind of error ended a failed run, so the Errors page can tell one MAM refusal that stopped every MAM job from
/// unrelated failures. Stored as a number, so new values go at the end.
/// </summary>
public enum JobFailureKind
{
    Other,

    /// <summary>MAM refused `mam_id` (HTTP 403).</summary>
    MamRefused,

    /// <summary>MAM blocked the account (HTTP 406).</summary>
    MamBlocked,

    /// <summary>MAM asked for fewer requests (HTTP 429).</summary>
    MamRateLimited,
}
