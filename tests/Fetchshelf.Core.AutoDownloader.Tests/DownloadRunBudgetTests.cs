namespace Fetchshelf.Core.AutoDownloader.Tests;

public class DownloadRunBudgetTests
{
    [Fact]
    public void Started_RatioCost_TakesASlotAndItsSizeFromTheRatioBudget()
    {
        var budget = new DownloadRunBudget(unsatLimit: 10, unsatCount: 2, wedgesAvailable: 5, ratioBudgetBytes: 1_000);

        budget.Started(SelectedTorrentCost.Ratio, sizeBytes: 300);

        Assert.Equal(3, budget.UnsatCount);
        Assert.Equal(700, budget.RatioBudgetBytes);
    }

    [Theory]
    [InlineData(SelectedTorrentCost.Vip)]
    [InlineData(SelectedTorrentCost.PersonalFreeleech)]
    [InlineData(SelectedTorrentCost.GlobalFreeleech)]
    public void Started_FreeCost_TakesASlotButNoRatio(SelectedTorrentCost cost)
    {
        var budget = new DownloadRunBudget(unsatLimit: 10, unsatCount: 2, wedgesAvailable: 5, ratioBudgetBytes: 1_000);

        budget.Started(cost, sizeBytes: 300);

        Assert.Equal(3, budget.UnsatCount);
        Assert.Equal(1_000, budget.RatioBudgetBytes);
    }

    [Theory]
    [InlineData(SelectedTorrentCost.UseWedge, 4)]
    [InlineData(SelectedTorrentCost.TryWedge, 4)]
    [InlineData(SelectedTorrentCost.Ratio, 5)]
    public void Failed_WedgeCost_AssumesTheWedgeWasSpent(SelectedTorrentCost cost, long expectedWedges)
    {
        var budget = new DownloadRunBudget(unsatLimit: 10, unsatCount: 2, wedgesAvailable: 5, ratioBudgetBytes: 1_000);

        budget.Failed(cost);

        Assert.Equal(expectedWedges, budget.WedgesAvailable);
    }

    [Theory]
    [InlineData(SelectedTorrentCost.Ratio, true)]
    [InlineData(SelectedTorrentCost.UseWedge, true)]
    [InlineData(SelectedTorrentCost.TryWedge, true)]
    [InlineData(SelectedTorrentCost.Vip, false)]
    [InlineData(SelectedTorrentCost.PersonalFreeleech, false)]
    [InlineData(SelectedTorrentCost.GlobalFreeleech, false)]
    public void SpendsRatio_EachCost_MatchesWhetherItIsFree(SelectedTorrentCost cost, bool expected) =>
        Assert.Equal(expected, DownloadBudget.SpendsRatio(cost));
}
