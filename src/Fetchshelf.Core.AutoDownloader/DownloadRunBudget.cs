namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// What is left of the account's limits during one download run. It starts from MAM's account state and is updated
/// after every torrent, so later torrents in the same run are checked against what earlier ones used up.
/// </summary>
public sealed class DownloadRunBudget(long unsatLimit, long unsatCount, long wedgesAvailable, long ratioBudgetBytes)
{
    public long UnsatLimit { get; } = unsatLimit;

    public long UnsatCount { get; private set; } = unsatCount;

    public long WedgesAvailable { get; private set; } = wedgesAvailable;

    public long RatioBudgetBytes { get; private set; } = ratioBudgetBytes;

    /// <summary>MAM spends the wedge when the torrent file is fetched, whatever happens to the torrent after that.</summary>
    public void WedgeSpent() => WedgesAvailable--;

    /// <summary>A started torrent takes an unsatisfied-snatch slot and, unless it is free, part of the ratio budget.</summary>
    public void Started(SelectedTorrentCost cost, long sizeBytes)
    {
        UnsatCount++;
        if (DownloadBudget.SpendsRatio(cost))
        {
            RatioBudgetBytes -= sizeBytes;
        }
    }

    /// <summary>
    /// A torrent failed part-way. The failure may have come after the wedge was fetched, which spends it on MAM, so a
    /// wedge cost is assumed spent: the rest of the run cannot dip below wedge_buffer.
    /// </summary>
    public void Failed(SelectedTorrentCost cost)
    {
        if (cost is SelectedTorrentCost.UseWedge or SelectedTorrentCost.TryWedge)
        {
            WedgesAvailable--;
        }
    }
}
