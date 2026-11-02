namespace Fetchshelf.Core.Jobs;

/// <summary>The outcome of a single job run.</summary>
public enum JobRunResult
{
    Success,
    Failure,

    /// <summary>The run went through to the end, but some of its items failed. See <see cref="JobCompletedWithErrorsException"/>.</summary>
    CompletedWithErrors,
}

/// <summary>A record of one completed job run.</summary>
public sealed record JobRunRecord(DateTimeOffset StartedAt, JobRunResult Result, string? ErrorMessage);
