namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// The downloader's ratio/unsat/wedge safety checks. Pure numeric functions
/// over primitives so they're testable without a real `MamAccountState`.
/// </summary>
public static class DownloadBudget
{
    private static readonly HashSet<SelectedTorrentCost> FreeCosts =
    [
        SelectedTorrentCost.Vip, SelectedTorrentCost.PersonalFreeleech, SelectedTorrentCost.GlobalFreeleech,
    ];

    public static bool IsFreeCost(SelectedTorrentCost cost) => FreeCosts.Contains(cost);

    /// <summary>
    /// Whether a download at this cost counts against the ratio budget. Wedge costs count too: a TryWedge may end up
    /// downloading without one, and the budget stays on the safe side for UseWedge.
    /// </summary>
    public static bool SpendsRatio(SelectedTorrentCost cost) => !IsFreeCost(cost);

    /// <summary>Ratio budget = (uploaded − downloaded − bytes already in flight) / minRatio.</summary>
    public static long ComputeRatioBudgetBytes(long uploadedBytes, long downloadedBytes, long bytesInFlight, double minRatio) =>
        (long)Math.Max(0, (uploadedBytes - downloadedBytes - bytesInFlight) / minRatio);

    /// <summary>
    /// A Ratio-cost torrent must not exceed the remaining ratio budget, so no download pushes the ratio
    /// below `min_ratio`; free costs ignore the budget entirely.
    /// </summary>
    public static bool FitsRatioBudget(SelectedTorrentCost cost, long torrentSizeBytes, long ratioBudgetBytes) =>
        IsFreeCost(cost) || torrentSizeBytes <= ratioBudgetBytes;

    /// <summary>
    /// A torrent's own unsat buffer requirement must leave at least that many free unsat slots after
    /// starting it: never use the last `unsat_buffer` unsatisfied-snatch slots.
    /// </summary>
    public static bool HasFreeUnsatSlot(long unsatLimit, long unsatCount, long unsatBuffer) =>
        unsatLimit - unsatCount > unsatBuffer;

    /// <summary>
    /// UseWedge fails outright at/below the buffer. TryWedge degrades to "no wedge" at/below the
    /// buffer or when the torrent is already free.
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
