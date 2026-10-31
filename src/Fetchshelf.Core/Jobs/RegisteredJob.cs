namespace Fetchshelf.Core.Jobs;

/// <summary>A background job registered by name, with its most recent run record.</summary>
public sealed class RegisteredJob
{
    public required string Name { get; init; }

    public required Func<CancellationToken, Task> RunAsync { get; init; }

    /// <summary>Brief, user-facing description of what this job does. Null if none was given.</summary>
    public string? Description { get; init; }

    public JobRunRecord? LastRun { get; internal set; }

    /// <summary>When the interval scheduler will next trigger this job. Null if the job has no timer.</summary>
    public DateTimeOffset? NextScheduledRun { get; internal set; }

    /// <summary>Whether this job's <see cref="RunAsync"/> is currently executing.</summary>
    public bool IsRunning { get; internal set; }
}
