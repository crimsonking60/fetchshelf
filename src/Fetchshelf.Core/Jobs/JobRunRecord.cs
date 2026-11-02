namespace Fetchshelf.Core.Jobs;

public enum JobRunResult
{
    Success,
    Failure,

    /// <summary>The run went through to the end, but some of its items failed. See <see cref="JobCompletedWithErrorsException"/>.</summary>
    CompletedWithErrors,
}

public sealed record JobRunRecord(DateTimeOffset StartedAt, JobRunResult Result, string? ErrorMessage);
