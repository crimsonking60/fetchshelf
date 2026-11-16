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
    [InlineData(SelectedTorrentCost.UseWedge, false)]
    [InlineData(SelectedTorrentCost.TryWedge, true)]
    [InlineData(SelectedTorrentCost.Vip, false)]
    [InlineData(SelectedTorrentCost.PersonalFreeleech, false)]
    [InlineData(SelectedTorrentCost.GlobalFreeleech, false)]
    public void SpendsRatio_EachCost_MatchesWhetherItMayDownloadOnRatio(SelectedTorrentCost cost, bool expected) =>
        Assert.Equal(expected, DownloadBudget.SpendsRatio(cost));

    [Theory]
    [InlineData(SelectedTorrentCost.Ratio, false, 900)]
    [InlineData(SelectedTorrentCost.TryWedge, false, 900)]
    [InlineData(SelectedTorrentCost.TryWedge, true, 1000)]
    [InlineData(SelectedTorrentCost.UseWedge, true, 1000)]
    [InlineData(SelectedTorrentCost.GlobalFreeleech, false, 1000)]
    public void Started_TakesTheRatioBudgetOnlyForATorrentOnRatio(SelectedTorrentCost cost, bool withWedge, long budgetLeft)
    {
        var budget = new DownloadRunBudget(unsatLimit: 10, unsatCount: 0, wedgesAvailable: 5, ratioBudgetBytes: 1000);

        budget.Started(cost, sizeBytes: 100, withWedge);

        Assert.Equal((1L, budgetLeft), (budget.UnsatCount, budget.RatioBudgetBytes));
    }
}
