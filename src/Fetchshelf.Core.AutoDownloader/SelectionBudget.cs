namespace Fetchshelf.Core.AutoDownloader;

/// <summary>Computes an autograbber run's selection budget.</summary>
public static class SelectionBudget
{
    /// <summary>
    /// Budget = unsat limit − unsat count − buffer, floored at 0, further capped by
    /// (`maxActiveDownloads` − currently active for this grabber) when `maxActiveDownloads` is set.
    /// </summary>
    public static long Compute(
        long unsatLimit, long unsatCount, long unsatBuffer, long? maxActiveDownloads, long currentActiveForGrabber)
    {
        var budget = Math.Max(0, unsatLimit - unsatCount - unsatBuffer);

        if (maxActiveDownloads is { } cap)
        {
            var remainingCap = Math.Max(0, cap - currentActiveForGrabber);
            budget = Math.Min(budget, remainingCap);
        }

        return budget;
    }

    /// <summary>
    /// Where <see cref="Compute"/>'s number comes from, for log lines: the free unsatisfied-snatch slots, or
    /// `max_active_downloads` when that is the tighter cap.
    /// </summary>
    public static string Explain(
        long unsatLimit, long unsatCount, long unsatBuffer, long? maxActiveDownloads, long currentActiveForGrabber)
    {
        var unsatBudget = Math.Max(0, unsatLimit - unsatCount - unsatBuffer);
        if (maxActiveDownloads is { } cap && Math.Max(0, cap - currentActiveForGrabber) < unsatBudget)
        {
            return $"max_active_downloads {cap} - {currentActiveForGrabber} already queued";
        }

        return $"unsatisfied-snatch slots: {unsatLimit} allowed - {unsatCount} in use - unsat_buffer {unsatBuffer}";
    }
}
