namespace Fetchshelf.Core.AutoDownloader.Tests;

public class DownloadBudgetTests
{
    [Fact]
    public void RatioCost_OversizedTorrent_DoesNotFitBudget()
    {
        var ratioBudget = DownloadBudget.ComputeRatioBudgetBytes(uploadedBytes: 100, downloadedBytes: 0, bytesInFlight: 0, minRatio: 2);

        Assert.False(DownloadBudget.FitsRatioBudget(SelectedTorrentCost.Ratio, torrentSizeBytes: 100, ratioBudget));
    }

    [Theory]
    [InlineData(SelectedTorrentCost.Vip)]
    [InlineData(SelectedTorrentCost.PersonalFreeleech)]
    [InlineData(SelectedTorrentCost.GlobalFreeleech)]
    public void FitsRatioBudget_FreeCost_IgnoresRatioBudget(SelectedTorrentCost cost)
    {
        Assert.True(DownloadBudget.FitsRatioBudget(cost, torrentSizeBytes: 1_000_000, ratioBudgetBytes: 0));
    }

    [Theory]
    [InlineData(10, 9, 0, true)]
    [InlineData(10, 10, 0, false)]
    [InlineData(10, 8, 1, true)]
    [InlineData(10, 9, 1, false)]
    public void HasFreeUnsatSlot_CountNearLimitAndBuffer_ReturnsWhetherASlotIsLeft(long limit, long count, long buffer, bool expected)
    {
        Assert.Equal(expected, DownloadBudget.HasFreeUnsatSlot(limit, count, buffer));
    }

    [Theory]
    [InlineData(5, 5, WedgeDecision.Fail)]
    [InlineData(4, 5, WedgeDecision.Fail)]
    [InlineData(6, 5, WedgeDecision.UseWedge)]
    public void DecideWedge_UseWedgeAtOrBelowBuffer_Fails(long wedgesAvailable, long wedgeBuffer, WedgeDecision expected)
    {
        var decision = DownloadBudget.DecideWedge(SelectedTorrentCost.UseWedge, wedgesAvailable, wedgeBuffer, isAlreadyFree: false);

        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(5, 5, false, WedgeDecision.SkipWedge)]
    [InlineData(6, 5, false, WedgeDecision.UseWedge)]
    [InlineData(10, 0, true, WedgeDecision.SkipWedge)]
    public void DecideWedge_TryWedgeWithoutSpareWedgeOrAlreadyFree_SkipsTheWedge(long wedgesAvailable, long wedgeBuffer, bool isAlreadyFree, WedgeDecision expected)
    {
        var decision = DownloadBudget.DecideWedge(SelectedTorrentCost.TryWedge, wedgesAvailable, wedgeBuffer, isAlreadyFree);

        Assert.Equal(expected, decision);
    }
}
