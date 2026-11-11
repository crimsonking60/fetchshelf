using Fetchshelf.Core.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Notifications;

/// <summary>
/// Sends <see cref="NotificationEvent.BudgetSpent"/> when a Download run leaves selections waiting for budget after one
/// that left none. How many the last run left is kept in the database, and only a run that got as far as the budget
/// changes it: a run that failed, or was cancelled, in between doesn't make the next one look new.
/// </summary>
public sealed class BudgetSpentWatcher(NotificationStateStore states, INotificationSink notifications, TimeProvider timeProvider, ILogger<BudgetSpentWatcher>? logger = null)
{
    /// <summary>The key how many selections the last Download run left waiting is stored under.</summary>
    public const string StateKey = "budget_spent.left_queued";

    private readonly ILogger<BudgetSpentWatcher> _logger = logger ?? NullLogger<BudgetSpentWatcher>.Instance;

    /// <summary>Checks one Download run's result. It never throws: it runs at the end of the job.</summary>
    /// <param name="leftQueued">How many selections the run left queued because a budget is spent.</param>
    /// <param name="limitedBy">Which budget, and what it came to.</param>
    public async Task CheckAsync(int leftQueued, string? limitedBy)
    {
        if (!notifications.Wants(NotificationEvent.BudgetSpent))
        {
            return;
        }

        try
        {
            var previous = await states.GetAsync(StateKey) ?? 0;
            if (previous != leftQueued)
            {
                await states.SetAsync(StateKey, leftQueued);
            }

            if (AccountNotificationRules.BudgetSpent(leftQueued, limitedBy, previous, timeProvider.GetUtcNow()) is { } notification)
            {
                notifications.Publish(notification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check the selections waiting for budget: {Error}", ex.Message);
        }
    }
}
