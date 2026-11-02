namespace Fetchshelf.Core.Jobs;

/// <summary>
/// Thrown by a job at the very end of a run that did all its work but had items fail, such as a link run
/// where some torrents could not be linked. The registry records the run as
/// <see cref="JobRunResult.CompletedWithErrors"/> rather than <see cref="JobRunResult.Failure"/>.
/// </summary>
public sealed class JobCompletedWithErrorsException(string message) : Exception(message);
