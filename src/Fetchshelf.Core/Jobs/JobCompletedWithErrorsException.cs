namespace Fetchshelf.Core.Jobs;

/// <summary>
/// Thrown by a job at the very end of a run that did all its work but had items fail, such as a link run
/// where some torrents could not be linked. The registry records the run as
/// <see cref="JobRunResult.CompletedWithErrors"/> rather than <see cref="JobRunResult.Failure"/>, with the message
/// as the run's summary.
/// </summary>
public sealed class JobCompletedWithErrorsException(string message, IReadOnlyDictionary<string, long>? counts = null) : Exception(message)
{
    /// <summary>The run's numbers by name, as in <see cref="JobOutcome.Counts"/>.</summary>
    public IReadOnlyDictionary<string, long>? Counts { get; } = counts;
}
