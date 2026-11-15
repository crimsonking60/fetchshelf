using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class BonusRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-bonus-runner-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string Account(double points) => $$"""
        {
            "ratio": 5.0, "uploaded_bytes": 1000, "downloaded_bytes": 100, "wedges": 10, "uid": 1, "seedbonus": {{points}},
            "snatch_summary": { "unsat": { "count": 0, "limit": 20 } }
        }
        """;

    private static string Bought(double pointsLeft) => $$"""{"success":true,"seedbonus":{{pointsLeft}}}""";

    private const string RateLimited = """{"success":false,"error":"Rate Limit Exceeded."}""";

    private sealed class Harness(FetchshelfDbContext context, MamAccessGate gate, List<string> purchases, Func<BonusSettings, BonusRunner> create)
    {
        public FetchshelfDbContext Context => context;

        public MamAccessGate Gate => gate;

        /// <summary>The query of every purchase sent to MAM, such as "spendtype=upload&amp;amount=50".</summary>
        public List<string> Purchases => purchases;

        public Task<BonusRunSummary> RunAsync(BonusSettings settings) => create(settings).RunAsync();
    }

    /// <param name="points">The points the account read gives.</param>
    /// <param name="answers">MAM's answers to the purchases, in order.</param>
    /// <param name="onPurchase">Called when a purchase is sent, such as to check the gate is held.</param>
    private Harness CreateHarness(
        double points, IEnumerable<string>? answers = null, ListLogger<BonusRunner>? logger = null, Action? onPurchase = null)
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var gate = new MamAccessGate();
        var queue = new Queue<string>(answers ?? []);
        var purchases = new List<string>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Account(points)) };
            }

            onPurchase?.Invoke();
            purchases.Add(uri.Query.TrimStart('?')[..uri.Query.TrimStart('?').IndexOf("&_=", StringComparison.Ordinal)]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(queue.Dequeue()) };
        });
        var mamClient = new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);
        return new Harness(context, gate, purchases, settings => new BonusRunner(
            context, new EventStore(context), mamClient, gate, settings, new Clock(Now), logger));
    }

    private static BonusSettings Settings(
        long keepPoints = 0, int[]? upload = null, int wedgeEvery = 0, bool vip = false, bool dryRun = false) => new()
        {
            KeepPoints = keepPoints,
            UploadAmounts = [.. upload ?? [100, 50]],
            WedgeEvery = wedgeEvery,
            Vip = vip,
            DryRun = dryRun,
        };

    [Fact]
    public async Task RunAsync_LikeTheShellScript_BuysAWedgeThenUploadWhileAboveKeepPoints()
    {
        var harness = CreateHarness(99_999, [Bought(49_999)]);

        var summary = await harness.RunAsync(Settings(keepPoints: 55_000, wedgeEvery: 24));

        Assert.Equal(["spendtype=wedges&source=points"], harness.Purchases);
        Assert.Equal(["a wedge"], summary.Bought);
        Assert.Null(summary.Failure);
        var purchase = await harness.Context.Events.SingleAsync();
        Assert.Equal(
            (EventKind.BonusPurchase, "Wedge", (int?)null, (double?)99_999, (double?)49_999),
            (purchase.Kind, purchase.BonusSpend, purchase.BonusUploadGb, purchase.PointsBefore, purchase.PointsAfter));
    }

    [Fact]
    public async Task RunAsync_UploadPurchases_GoByThePointsMamSaysAreLeft()
    {
        // MAM's answers decide: after the 100 GB, 30,000 are left above keep_points 10,000, which pays for 50 GB once.
        var harness = CreateHarness(99_999, [Bought(40_000), Bought(15_000)]);

        var summary = await harness.RunAsync(Settings(keepPoints: 10_000));

        Assert.Equal(["spendtype=upload&amount=100", "spendtype=upload&amount=50"], harness.Purchases);
        Assert.Equal(["100 GB of upload credit", "50 GB of upload credit"], summary.Bought);
        Assert.Equal([100, 50], await harness.Context.Events.OrderBy(e => e.Id).Select(e => e.BonusUploadGb!.Value).ToListAsync());
    }

    [Fact]
    public async Task RunAsync_TakesTheGateAsBonus()
    {
        MamAccessGate? gate = null;
        string? owner = null;
        var harness = CreateHarness(99_999, [Bought(49_999)], onPurchase: () => owner = gate!.Owner);
        gate = harness.Gate;

        await harness.RunAsync(Settings(upload: [100]));

        Assert.Equal(JobNames.Bonus, owner);
    }

    [Fact]
    public async Task RunAsync_DryRun_BuysNothingAndSaysWhatItWould()
    {
        var logger = new ListLogger<BonusRunner>();
        var harness = CreateHarness(99_999, logger: logger);

        var summary = await harness.RunAsync(Settings(keepPoints: 55_000, wedgeEvery: 24, upload: [20], dryRun: true));

        Assert.Empty(harness.Purchases);
        Assert.Empty(harness.Context.Events);
        Assert.True(summary.DryRun);
        Assert.Equal(["a wedge"], summary.Bought);
        Assert.Contains("Bonus: would buy a wedge for 50,000 points", logger.Messages(LogLevel.Information));
    }

    [Fact]
    public async Task RunAsync_PointsDidNotGoDown_StopsWithAFailureAndRecordsNothing()
    {
        var harness = CreateHarness(99_999, [Bought(99_999)]);

        var summary = await harness.RunAsync(Settings(upload: [100]));

        Assert.Single(harness.Purchases);
        Assert.Empty(harness.Context.Events);
        Assert.Equal(
            "MAM said 100 GB of upload credit was bought, but the points didn't go down (99,999 before, 99,999 after)",
            summary.Failure);
    }

    [Fact]
    public async Task RunAsync_RateLimited_EndsThePurchasesWithoutAFailure()
    {
        var harness = CreateHarness(99_999, [Bought(49_999), RateLimited]);

        var summary = await harness.RunAsync(Settings(upload: [100, 1]));

        Assert.Equal(2, harness.Purchases.Count);
        Assert.Equal(["100 GB of upload credit"], summary.Bought);
        Assert.Null(summary.Failure);
        Assert.Equal("MAM refused 1 GB of upload credit: Rate Limit Exceeded.; the next run tries again", summary.StopReason);
    }

    [Fact]
    public async Task RunAsync_UploadRefusedForAnotherReason_StopsWithAFailure()
    {
        var harness = CreateHarness(99_999, ["""{"success":false,"error":"Invalid amount"}"""]);

        var summary = await harness.RunAsync(Settings(upload: [100]));

        Assert.Equal("MAM refused 100 GB of upload credit: Invalid amount", summary.Failure);
    }

    [Fact]
    public async Task RunAsync_VipRefused_GoesOnToUploadCredit()
    {
        var harness = CreateHarness(99_999, ["""{"success":false,"error":"You are already at the maximum"}""", Bought(49_999)]);

        var summary = await harness.RunAsync(Settings(upload: [100], vip: true));

        Assert.Equal(["spendtype=VIP&duration=max", "spendtype=upload&amount=100"], harness.Purchases);
        Assert.Equal(["100 GB of upload credit"], summary.Bought);
        Assert.Null(summary.Failure);
    }

    [Fact]
    public async Task RunAsync_WedgeBoughtWithinWedgeEvery_IsNotBoughtAgain()
    {
        var harness = CreateHarness(99_999);
        harness.Context.Events.Add(new EventEntity
        {
            OccurredAt = Now - TimeSpan.FromHours(3),
            Kind = EventKind.BonusPurchase,
            BonusSpend = nameof(BonusPurchaseKind.Wedge),
        });
        await harness.Context.SaveChangesAsync();

        var summary = await harness.RunAsync(Settings(upload: [], wedgeEvery: 24));

        Assert.Empty(harness.Purchases);
        Assert.Equal("nothing is due", summary.StopReason);
    }

    [Fact]
    public async Task RunAsync_AccountReadHasNoPoints_Throws()
    {
        var context = SelectionTestSupport.CreateContext(DbPath);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ratio":5.0,"uploaded_bytes":1,"downloaded_bytes":1,"wedges":1,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}"""),
        });
        var mamClient = new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" });
        var runner = new BonusRunner(context, new EventStore(context), mamClient, new MamAccessGate(), Settings(), new Clock(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());
    }
}
