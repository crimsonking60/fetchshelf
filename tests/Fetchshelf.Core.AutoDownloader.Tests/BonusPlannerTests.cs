namespace Fetchshelf.Core.AutoDownloader.Tests;

public class BonusPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static BonusPolicy Policy(
        long keepPoints = 0, int[]? upload = null, int wedgeEveryHours = 0, bool vip = false, int maxPurchases = 10) =>
        new(keepPoints, upload ?? [100, 50, 20, 5, 1], wedgeEveryHours, vip, maxPurchases);

    private static BonusRunState State(double points, DateTimeOffset? lastWedgeAt = null) => new(points, Now, lastWedgeAt);

    [Fact]
    public void Next_PointsAboveKeepPoints_BuysTheLargestUploadTheyPayFor()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000), State(99_999));

        Assert.Equal(BonusPurchase.Upload(50), decision.Purchase);
    }

    [Fact]
    public void Next_UploadAmountsInAnyOrder_TriesTheLargestFirst()
    {
        var decision = BonusPlanner.Next(Policy(upload: [5, 100, 20]), State(60_000));

        Assert.Equal(BonusPurchase.Upload(100), decision.Purchase);
    }

    [Fact]
    public void Next_PointsAtKeepPoints_BuysNothingAndSaysWhy()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000), State(41_250.7));

        Assert.Null(decision.Purchase);
        Assert.Equal("the 41,250 points leave nothing above keep_points 55,000", decision.Reason);
    }

    [Fact]
    public void Next_SpareBelowTheSmallestAmount_SaysWhatItWouldCost()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000, upload: [50, 20]), State(60_000));

        Assert.Null(decision.Purchase);
        Assert.Equal("the 5,000 points above keep_points 55,000 don't pay for 20 GB (10,000)", decision.Reason);
    }

    [Fact]
    public void Next_NoUploadAmounts_BuysNoUpload()
    {
        var decision = BonusPlanner.Next(Policy(upload: []), State(99_999));

        Assert.Null(decision.Purchase);
        Assert.Equal("nothing is due", decision.Reason);
    }

    [Fact]
    public void Next_WedgeNeverBought_IsDueAndComesBeforeUpload()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000, wedgeEveryHours: 24), State(99_999));

        Assert.Equal(BonusPurchase.Wedge, decision.Purchase);
    }

    [Fact]
    public void Next_Wedge_MayUseThePointsKeptBack()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000, wedgeEveryHours: 24), State(52_000));

        Assert.Equal(BonusPurchase.Wedge, decision.Purchase);
    }

    [Fact]
    public void Next_WedgeDueButTooFewPoints_SaysSo()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000, wedgeEveryHours: 24), State(49_000));

        Assert.Null(decision.Purchase);
        Assert.Equal("a wedge is due, but 49,000 points are less than the 50,000 it costs", decision.Reason);
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(23.5, false)]
    [InlineData(23.84, true)] // within the ten minutes' slack
    [InlineData(24, true)]
    public void Next_WedgeBoughtHoursAgo_IsDueAfterWedgeEvery(double hoursAgo, bool due)
    {
        var decision = BonusPlanner.Next(
            Policy(upload: [], wedgeEveryHours: 24), State(99_999, lastWedgeAt: Now - TimeSpan.FromHours(hoursAgo)));

        Assert.Equal(due ? BonusPurchase.Wedge : null, decision.Purchase);
    }

    [Fact]
    public void Next_WedgeEveryZero_NeverBuysAWedge()
    {
        var decision = BonusPlanner.Next(Policy(upload: [], wedgeEveryHours: 0), State(99_999));

        Assert.Null(decision.Purchase);
    }

    [Fact]
    public void Next_Vip_ComesAfterTheWedgeAndBeforeUpload()
    {
        var policy = Policy(wedgeEveryHours: 24, vip: true);

        var first = BonusPlanner.Next(policy, State(99_999));
        var second = BonusPlanner.Next(policy, BonusPlanner.After(State(99_999), BonusPurchase.Wedge, 49_999));
        var third = BonusPlanner.Next(policy, BonusPlanner.After(
            BonusPlanner.After(State(99_999), BonusPurchase.Wedge, 49_999), BonusPurchase.Vip, 40_000));

        Assert.Equal(
            new List<BonusPurchase?> { BonusPurchase.Wedge, BonusPurchase.Vip, BonusPurchase.Upload(50) },
            new List<BonusPurchase?> { first.Purchase, second.Purchase, third.Purchase });
    }

    [Fact]
    public void Next_VipWithLessThanItsReserveAboveKeepPoints_IsNotTried()
    {
        var decision = BonusPlanner.Next(Policy(keepPoints: 55_000, upload: [], vip: true), State(69_000));

        Assert.Null(decision.Purchase);
    }

    [Fact]
    public void Next_MaxPurchasesMade_Stops()
    {
        var state = State(99_999) with { Purchases = 2 };

        var decision = BonusPlanner.Next(Policy(maxPurchases: 2), state);

        Assert.Null(decision.Purchase);
        Assert.Equal("this run made its 2 purchases (max_purchases_per_run)", decision.Reason);
    }

    [Fact]
    public void PlanRun_LikeTheShellScript_BuysAWedgeThenUploadDownToKeepPoints()
    {
        var plan = BonusPlanner.PlanRun(Policy(keepPoints: 55_000, upload: [100, 50], wedgeEveryHours: 24), State(99_999));

        // 99,999 - 50,000 for the wedge leaves 49,999: nothing above 55,000 for upload.
        Assert.Equal([BonusPurchase.Wedge], plan.Purchases);
        Assert.Equal("the 49,999 points leave nothing above keep_points 55,000", plan.StopReason);
    }

    [Fact]
    public void PlanRun_ManyPointsAboveKeepPoints_BuysLargestFirstThenSmaller()
    {
        var plan = BonusPlanner.PlanRun(Policy(keepPoints: 10_000), State(99_999));

        // 89,999 above keep_points: 100 GB (50,000), 50 GB (25,000), 20 GB (10,000), 5 GB (2,500), then 1 GB × 4.
        Assert.Equal(
            [BonusPurchase.Upload(100), BonusPurchase.Upload(50), BonusPurchase.Upload(20), BonusPurchase.Upload(5),
             BonusPurchase.Upload(1), BonusPurchase.Upload(1), BonusPurchase.Upload(1), BonusPurchase.Upload(1)],
            plan.Purchases);
        Assert.Equal("the 499 points above keep_points 10,000 don't pay for 1 GB (500)", plan.StopReason);
    }

    [Fact]
    public void PlanRun_StopsAtMaxPurchasesPerRun()
    {
        var plan = BonusPlanner.PlanRun(Policy(upload: [1], maxPurchases: 3), State(99_999));

        Assert.Equal(3, plan.Purchases.Count);
    }

    [Theory]
    [InlineData(BonusPurchaseKind.Wedge, 0, 50_000, "a wedge")]
    [InlineData(BonusPurchaseKind.Vip, 0, 15_000, "a VIP top-up")]
    [InlineData(BonusPurchaseKind.Upload, 50, 25_000, "50 GB of upload credit")]
    public void Purchase_PriceAndDescription(BonusPurchaseKind kind, int gb, long price, string description)
    {
        var purchase = new BonusPurchase(kind, gb);

        Assert.Equal((price, description), (purchase.Price, purchase.Description));
    }
}
