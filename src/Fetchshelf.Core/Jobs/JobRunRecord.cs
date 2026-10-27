namespace Fetchshelf.Core.Jobs;

/// <summary>The outcome of a single job run, Fetchshelf-spec.md §10.1 ("the last start time and result").</summary>
public enum JobRunResult
{
    Success,
    Failure,
}

/// <summary>A record of one completed job run.</summary>
public sealed record JobRunRecord(DateTimeOffset StartedAt, JobRunResult Result, string? ErrorMessage);
