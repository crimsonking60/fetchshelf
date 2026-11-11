using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Data.Entities;

/// <summary>One run of a background job, kept so the Jobs page and its history survive a restart.</summary>
public sealed class JobRunEntity
{
    public long Id { get; set; }

    /// <summary>The job's registered name.</summary>
    public required string JobName { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the run is going, and for a run Fetchshelf stopped during without recording when.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public required JobRunResult Result { get; set; }

    public required JobTrigger Trigger { get; set; }

    /// <summary>The run's one-line summary, or the error that ended it.</summary>
    public string? Summary { get; set; }

    /// <summary>The numbers behind the summary, by name, as a JSON object.</summary>
    public string? CountsJson { get; set; }

    /// <summary>What kind of error ended a failed run; null for any other result.</summary>
    public JobFailureKind? FailureKind { get; set; }
}
