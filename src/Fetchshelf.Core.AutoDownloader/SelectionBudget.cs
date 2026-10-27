namespace Fetchshelf.Core.AutoDownloader;

/// <summary>Computes an autograbber run's selection budget, Fetchshelf-spec.md §7.1 step 2.</summary>
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
}
