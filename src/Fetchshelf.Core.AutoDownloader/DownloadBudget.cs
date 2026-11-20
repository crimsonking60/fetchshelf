using System.Globalization;

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
    /// Whether a download at this cost may spend ratio, and so counts against the ratio budget: Ratio, and TryWedge,
    /// which downloads without a wedge when none is left above `wedge_buffer`. UseWedge never does: it spends a wedge or
    /// stays queued.
    /// </summary>
    public static bool SpendsRatio(SelectedTorrentCost cost) => !IsFreeCost(cost) && cost != SelectedTorrentCost.UseWedge;

    /// <summary>
    /// Ratio budget = uploaded / minRatio − downloaded − bytes already in flight: the largest x for which
    /// uploaded / (downloaded + in flight + x) stays at or above minRatio. Never negative, and capped at
    /// <see cref="long.MaxValue"/> so a minRatio of 0 gives "no limit" rather than an undefined cast.
    /// </summary>
    public static long ComputeRatioBudgetBytes(double uploadedBytes, double downloadedBytes, long bytesInFlight, double minRatio)
    {
        var budget = uploadedBytes / minRatio - downloadedBytes - bytesInFlight;
        if (double.IsNaN(budget) || budget <= 0)
        {
            return 0;
        }

        return budget >= long.MaxValue ? long.MaxValue : (long)budget;
    }

    /// <summary>The run's ratio budget and what it was worked out from, for the log.</summary>
    public static string DescribeRatioBudget(double uploadedBytes, double downloadedBytes, long bytesInFlight, double minRatio)
    {
        var budget = ComputeRatioBudgetBytes(uploadedBytes, downloadedBytes, bytesInFlight, minRatio);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Ratio budget for this run: {FormatBytes(budget)} (uploaded {FormatBytes(uploadedBytes)}, downloaded {FormatBytes(downloadedBytes)}, " +
            $"{FormatBytes(bytesInFlight)} in flight, min_ratio {minRatio})");
    }

    /// <summary>A byte count as the UI and the log show it: "436.0 GiB", or "unlimited".</summary>
    public static string FormatBytes(double bytes)
    {
        if (bytes >= long.MaxValue)
        {
            return "unlimited";
        }

        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes:0} B")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes:0.0} {units[unit]}");
    }

    /// <summary>
    /// A torrent that may spend ratio (<see cref="SpendsRatio"/>) must not exceed the remaining ratio budget, so no
    /// download pushes the ratio below `min_ratio`; free costs and UseWedge ignore the budget entirely.
    /// </summary>
    public static bool FitsRatioBudget(SelectedTorrentCost cost, long torrentSizeBytes, long ratioBudgetBytes) =>
        !SpendsRatio(cost) || torrentSizeBytes <= ratioBudgetBytes;

    /// <summary>
    /// A torrent's own unsat buffer requirement must leave at least that many free unsat slots after
    /// starting it: never use the last `unsat_buffer` unsatisfied-snatch slots.
    /// </summary>
    public static bool HasFreeUnsatSlot(long unsatLimit, long unsatCount, long unsatBuffer) =>
        unsatLimit - unsatCount > unsatBuffer;

    /// <summary>
    /// A torrent that is already free needs no wedge whatever the cost. Otherwise UseWedge fails outright at/below the
    /// buffer, and TryWedge degrades to "no wedge" there.
    /// </summary>
    public static WedgeDecision DecideWedge(SelectedTorrentCost cost, long wedgesAvailable, long wedgeBuffer, bool isAlreadyFree)
    {
        var atOrBelowBuffer = wedgesAvailable <= wedgeBuffer;

        return cost switch
        {
            SelectedTorrentCost.UseWedge => isAlreadyFree ? WedgeDecision.SkipWedge
                : atOrBelowBuffer ? WedgeDecision.Fail : WedgeDecision.UseWedge,
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
