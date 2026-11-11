namespace Fetchshelf.Core.Jobs;

/// <summary>
/// Hears every job run that ends. Listeners are given to the <see cref="JobRegistry"/> when it is built.
/// </summary>
public interface IJobRunListener
{
    /// <summary>
    /// Called on the job's own thread, right after the run is recorded and before the job counts as idle, so it must
    /// not wait on anything: a listener that needs to do slow work queues it. A listener that throws is logged and
    /// skipped; it can't fail the job.
    /// </summary>
    void RunFinished(JobRunFinished finished);
}
