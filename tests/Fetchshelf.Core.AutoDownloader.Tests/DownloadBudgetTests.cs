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
    [InlineData(100, 40, 0, 2, 10)]
    [InlineData(100, 40, 5, 2, 5)]
    [InlineData(100, 60, 0, 2, 0)] // below the floor
    [InlineData(100, 50, 0, 2, 0)] // exactly at the floor
    [InlineData(100, 0, 0, 2, 50)]
    [InlineData(100, 40, 0, 1, 60)]
    [InlineData(100, 40, 0, 0.5, 160)]
    public void ComputeRatioBudgetBytes_LeavesTheRatioAtMinRatio(
        double uploaded, double downloaded, long inFlight, double minRatio, long expected)
    {
        Assert.Equal(expected, DownloadBudget.ComputeRatioBudgetBytes(uploaded, downloaded, inFlight, minRatio));
    }

    [Theory]
    [InlineData(250_000_000_000, 40_000_000_000, 3_000_000_000, 2)]
    [InlineData(80_000_000_000, 30_000_000_000, 0, 1.5)]
    [InlineData(1_000_000_000_000, 900_000_000_000, 10_000_000_000, 1)]
    public void ComputeRatioBudgetBytes_SpendingTheWholeBudgetStaysAtMinRatioAndOneByteMoreWouldNot(
        double uploaded, double downloaded, long inFlight, double minRatio)
    {
        var budget = DownloadBudget.ComputeRatioBudgetBytes(uploaded, downloaded, inFlight, minRatio);

        Assert.True(uploaded / (downloaded + inFlight + budget) >= minRatio);
        Assert.True(uploaded / (downloaded + inFlight + budget + 1) < minRatio);
    }

    [Fact]
    public void ComputeRatioBudgetBytes_MinRatioZero_IsUnlimitedRatherThanAnUndefinedCast()
    {
        Assert.Equal(long.MaxValue, DownloadBudget.ComputeRatioBudgetBytes(100, 40, 0, minRatio: 0));
    }

    [Fact]
    public void DescribeRatioBudget_NamesTheBudgetAndWhatItCameFrom()
    {
        const double GiB = 1024d * 1024 * 1024;

        var text = DownloadBudget.DescribeRatioBudget(100 * GiB, 40 * GiB, 0, 2);

        Assert.Equal("Ratio budget for this run: 10.0 GiB (uploaded 100.0 GiB, downloaded 40.0 GiB, 0 B in flight, min_ratio 2)", text);
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

    [Fact]
    public void ChangeableByHand_EveryCost_OffersExactlyTheCostsThatSpendRatio() =>
        Assert.Equal(
            Enum.GetValues<SelectedTorrentCost>().Where(DownloadBudget.SpendsRatio).Order(),
            SelectedTorrentCosts.ChangeableByHand.Order());
}
