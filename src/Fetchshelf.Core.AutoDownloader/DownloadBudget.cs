namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// The downloader's ratio/unsat/wedge safety checks, Fetchshelf-spec.md §7.3, §13. Pure numeric functions
/// over primitives so they're testable without a real `MamAccountState`.
/// </summary>
public static class DownloadBudget
{
    private static readonly HashSet<SelectedTorrentCost> FreeCosts =
    [
        SelectedTorrentCost.Vip, SelectedTorrentCost.PersonalFreeleech, SelectedTorrentCost.GlobalFreeleech,
    ];

    public static bool IsFreeCost(SelectedTorrentCost cost) => FreeCosts.Contains(cost);

    /// <summary>Ratio budget = (uploaded − downloaded − bytes already in flight) / minRatio.</summary>
    public static long ComputeRatioBudgetBytes(long uploadedBytes, long downloadedBytes, long bytesInFlight, double minRatio) =>
        (long)Math.Max(0, (uploadedBytes - downloadedBytes - bytesInFlight) / minRatio);

    /// <summary>
    /// A Ratio-cost torrent must not exceed the remaining ratio budget; free costs ignore it
    /// entirely (Fetchshelf-spec.md §13 "Never start a download that would push ratio below `min_ratio`").
    /// </summary>
    public static bool FitsRatioBudget(SelectedTorrentCost cost, long torrentSizeBytes, long ratioBudgetBytes) =>
        IsFreeCost(cost) || torrentSizeBytes <= ratioBudgetBytes;

    /// <summary>
    /// A torrent's own unsat buffer requirement must leave at least that many free unsat slots after
    /// starting it (Fetchshelf-spec.md §13 "Never ... use the last `unsat_buffer` unsat slots").
    /// </summary>
    public static bool HasFreeUnsatSlot(long unsatLimit, long unsatCount, long unsatBuffer) =>
        unsatLimit - unsatCount > unsatBuffer;

    /// <summary>
    /// UseWedge fails outright at/below the buffer. TryWedge degrades to "no wedge" at/below the
    /// buffer or when the torrent is already free (Fetchshelf-spec.md §7.3, §13).
    /// </summary>
    public static WedgeDecision DecideWedge(SelectedTorrentCost cost, long wedgesAvailable, long wedgeBuffer, bool isAlreadyFree)
    {
        var atOrBelowBuffer = wedgesAvailable <= wedgeBuffer;

        return cost switch
        {
            SelectedTorrentCost.UseWedge => atOrBelowBuffer ? WedgeDecision.Fail : WedgeDecision.UseWedge,
            SelectedTorrentCost.TryWedge => (atOrBelowBuffer || isAlreadyFree) ? WedgeDecision.SkipWedge : WedgeDecision.UseWedge,
            _ => WedgeDecision.SkipWedge,
        };
    }
}

public enum WedgeDecision
{
    UseWedge,
    SkipWedge,
    Fail,
}
