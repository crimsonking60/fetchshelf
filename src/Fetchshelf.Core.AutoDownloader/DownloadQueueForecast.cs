namespace Fetchshelf.Core.AutoDownloader;

/// <summary>A selection waiting for the Download job: its cost, size, and the autograb that queued it, if any.</summary>
public sealed record QueuedTorrent(SelectedTorrentCost Cost, long SizeBytes, string? GrabberName);

/// <summary>How much of the queue the account's numbers would let the Download job start.</summary>
public static class DownloadQueueForecast
{
    /// <summary>
    /// Goes through the queue in order with the Download job's own checks (a free unsatisfied-snatch slot beyond
    /// `unsat_buffer`, room in the ratio budget, a wedge beyond `wedge_buffer`) and counts the torrents that pass,
    /// using up the budget as the job would. An estimate: the job also finds torrents that are already in qBittorrent or
    /// no longer free, which only MAM and qBittorrent can say.
    /// </summary>
    public static int CountStartable(IEnumerable<QueuedTorrent> queue, DownloadRunBudget budget, SafetyBuffers buffers)
    {
        var startable = 0;
        foreach (var torrent in queue)
        {
            if (!DownloadBudget.HasFreeUnsatSlot(budget.UnsatLimit, budget.UnsatCount, buffers.UnsatBufferFor(torrent.GrabberName))
                || !DownloadBudget.FitsRatioBudget(torrent.Cost, torrent.SizeBytes, budget.RatioBudgetBytes))
            {
                continue;
            }

            if (torrent.Cost is SelectedTorrentCost.UseWedge or SelectedTorrentCost.TryWedge)
            {
                var decision = DownloadBudget.DecideWedge(
                    torrent.Cost, budget.WedgesAvailable, buffers.WedgeBufferFor(torrent.GrabberName), isAlreadyFree: false);
                if (decision == WedgeDecision.Fail)
                {
                    continue;
                }

                if (decision == WedgeDecision.UseWedge)
                {
                    budget.WedgeSpent();
                }
            }

            budget.Started(torrent.Cost, torrent.SizeBytes);
            startable++;
        }

        return startable;
    }
}
