using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class DownloadQueueForecastTests
{
    private static readonly SafetyBuffers NoBuffers = new(0, 0, []);

    private static DownloadRunBudget Budget(long unsatLimit = 20, long unsatCount = 0, long wedges = 10, long ratioBytes = 1_000) =>
        new(unsatLimit, unsatCount, wedges, ratioBytes);

    [Fact]
    public void CountStartable_EverythingFits_CountsTheWholeQueue() =>
        Assert.Equal(2, DownloadQueueForecast.CountStartable(
            [new(SelectedTorrentCost.Ratio, 100, null), new(SelectedTorrentCost.GlobalFreeleech, 5_000, null)], Budget(), NoBuffers));

    [Fact]
    public void CountStartable_RatioBudget_IsUsedUpAlongTheQueue()
    {
        // 600 + 600 would pass the 1,000 budget; the free one after them costs no ratio.
        QueuedTorrent[] queue =
        [
            new(SelectedTorrentCost.Ratio, 600, null),
            new(SelectedTorrentCost.Ratio, 600, null),
            new(SelectedTorrentCost.Vip, 600, null),
        ];

        Assert.Equal(2, DownloadQueueForecast.CountStartable(queue, Budget(), NoBuffers));
    }

    [Fact]
    public void CountStartable_UnsatSlots_StopAtTheTorrentsOwnBuffer()
    {
        // 20 allowed, 17 in use: the global buffer of 1 leaves 2 slots; "careful" keeps 3 free and gets none.
        var buffers = new SafetyBuffers(1, 0, [new AutograbBuffers("careful", 3, null)]);
        QueuedTorrent[] queue =
        [
            new(SelectedTorrentCost.Vip, 1, "careful"),
            new(SelectedTorrentCost.Vip, 1, null),
            new(SelectedTorrentCost.Vip, 1, null),
            new(SelectedTorrentCost.Vip, 1, null),
        ];

        Assert.Equal(2, DownloadQueueForecast.CountStartable(queue, Budget(unsatCount: 17), buffers));
    }

    [Fact]
    public void CountStartable_UseWedge_NeedsAWedgeBeyondTheBufferAndSpendsIt()
    {
        var buffers = new SafetyBuffers(0, 1, []);
        QueuedTorrent[] queue = [new(SelectedTorrentCost.UseWedge, 1, null), new(SelectedTorrentCost.UseWedge, 1, null)];

        Assert.Equal(1, DownloadQueueForecast.CountStartable(queue, Budget(wedges: 2), buffers));
    }

    [Fact]
    public void CountStartable_TryWedgeWithoutWedges_StillStartsWithinTheRatioBudget() =>
        Assert.Equal(1, DownloadQueueForecast.CountStartable([new(SelectedTorrentCost.TryWedge, 100, null)], Budget(wedges: 0), NoBuffers));
}
