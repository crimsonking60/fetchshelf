using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one run of the Bonus job did.</summary>
/// <param name="Bought">What was bought, in order, such as "a wedge"; in a dry run, what would have been.</param>
/// <param name="DryRun">Whether the run was a dry run, which buys nothing.</param>
/// <param name="StopReason">Why the run bought nothing more.</param>
/// <param name="Failure">Why the run stopped short with an error, such as a purchase MAM refused; null when it didn't.</param>
public sealed record BonusRunSummary(IReadOnlyList<string> Bought, bool DryRun, string StopReason, string? Failure = null);

/// <summary>
/// The Bonus job: spends the account's bonus points as `[bonus]` says, one purchase at a time (<see cref="BonusPlanner"/>):
/// a wedge when one is due, then a VIP top-up, then upload credit. Each purchase is recorded as an event.
/// </summary>
/// <remarks>
/// A purchase is never repeated: MAM's answer must show the points went down, or the run stops. A refusal because
/// purchases came too fast ends the run's purchases until the next run; a refused VIP top-up is logged and the run goes on.
/// </remarks>
public sealed class BonusRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    MamClient mamClient,
    MamAccessGate accessGate,
    BonusSettings settings,
    TimeProvider? timeProvider = null,
    ILogger<BonusRunner>? logger = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger<BonusRunner> _logger = logger ?? NullLogger<BonusRunner>.Instance;

    public async Task<BonusRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(JobNames.Bonus, cancellationToken);

        var account = await mamClient.RefreshAccountStateAsync(cancellationToken);
        if (account.BonusPoints is not { } points)
        {
            throw new InvalidOperationException("MAM's account answer has no bonus points (seedbonus), so there is nothing to spend.");
        }

        var policy = settings.ToPolicy();
        var state = new BonusRunState(points, _time.GetUtcNow(), await BonusPurchases.LastWedgeAtAsync(dbContext, cancellationToken));
        if (settings.DryRun)
        {
            return DryRun(policy, state);
        }

        var bought = new List<string>();
        try
        {
            return await BuyAsync(policy, state, bought, cancellationToken);
        }
        finally
        {
            if (bought.Count > 0)
            {
                await RereadAccountAsync(cancellationToken);
            }
        }
    }

    private BonusRunSummary DryRun(BonusPolicy policy, BonusRunState state)
    {
        var plan = BonusPlanner.PlanRun(policy, state);
        _logger.LogInformation("Dry run with {Points} points", BonusPlanner.Points(state.Points));
        foreach (var purchase in plan.Purchases)
        {
            _logger.LogInformation("Would buy {Purchase} for {Price} points", purchase.Description, BonusPlanner.Points(purchase.Price));
        }

        _logger.LogInformation("Then nothing more, as {Reason}", plan.StopReason);
        return new BonusRunSummary(plan.Purchases.Select(p => p.Description).ToList(), DryRun: true, plan.StopReason);
    }

    private async Task<BonusRunSummary> BuyAsync(BonusPolicy policy, BonusRunState state, List<string> bought, CancellationToken cancellationToken)
    {
        while (true)
        {
            var decision = BonusPlanner.Next(policy, state with { Now = _time.GetUtcNow() });
            if (decision.Purchase is not { } purchase)
            {
                _logger.LogInformation("Nothing more to buy, as {Reason}", decision.Reason);
                return new BonusRunSummary(bought, DryRun: false, decision.Reason);
            }

            var result = await mamClient.BuyWithBonusPointsAsync(ToSpend(purchase.Kind), purchase.UploadGb, cancellationToken);
            if (!result.Success)
            {
                var refusal = $"MAM refused {purchase.Description}: {result.Error ?? "it gave no reason"}";
                if (result.IsRateLimited)
                {
                    _logger.LogWarning("{Refusal}; the next run tries again", refusal);
                    return new BonusRunSummary(bought, DryRun: false, $"{refusal}; the next run tries again");
                }

                if (purchase.Kind == BonusPurchaseKind.Vip)
                {
                    _logger.LogInformation("{Refusal}", refusal);
                    state = state with { VipDone = true };
                    continue;
                }

                _logger.LogWarning("{Refusal}", refusal);
                return new BonusRunSummary(bought, DryRun: false, refusal, Failure: refusal);
            }

            if (result.PointsAfter is not { } after || after >= state.Points)
            {
                var noChange = $"MAM said {purchase.Description} was bought, but the points didn't go down "
                    + $"({BonusPlanner.Points(state.Points)} before, {(result.PointsAfter is { } left ? BonusPlanner.Points(left) : "none given")} after)";
                _logger.LogWarning("{Problem}; stopping", noChange);
                return new BonusRunSummary(bought, DryRun: false, noChange, Failure: noChange);
            }

            await eventStore.AppendAsync(new EventEntity
            {
                OccurredAt = _time.GetUtcNow(),
                Kind = EventKind.BonusPurchase,
                BonusSpend = purchase.Kind.ToString(),
                BonusUploadGb = purchase.Kind == BonusPurchaseKind.Upload ? purchase.UploadGb : null,
                PointsBefore = state.Points,
                PointsAfter = after,
            }, cancellationToken);
            _logger.LogInformation(
                "Bought {Purchase} with bonus points, {Before} → {After}",
                purchase.Description, BonusPlanner.Points(state.Points), BonusPlanner.Points(after));
            bought.Add(purchase.Description);
            state = BonusPlanner.After(state, purchase, after);
        }
    }

    /// <summary>Reads the account again, so the Account page shows the points, wedges and upload after the purchases.</summary>
    private async Task RereadAccountAsync(CancellationToken cancellationToken)
    {
        try
        {
            await mamClient.RefreshAccountStateAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not read the account after the purchases: {Error}", ex.Message);
        }
    }

    private static MamBonusSpend ToSpend(BonusPurchaseKind kind) => kind switch
    {
        BonusPurchaseKind.Wedge => MamBonusSpend.Wedge,
        BonusPurchaseKind.Vip => MamBonusSpend.Vip,
        _ => MamBonusSpend.Upload,
    };
}
