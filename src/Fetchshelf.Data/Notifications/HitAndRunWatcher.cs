using System.Globalization;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Notifications;

/// <summary>
/// Compares each account read with the last hit-and-run count seen, which is kept in the database, so one that appears
/// while Fetchshelf is down is still sent after the restart. The count is only compared and stored while some
/// `[[notify]]` block wants <see cref="NotificationEvent.HitAndRun"/>. It is stored when the notification is queued, not
/// when it is delivered: a message lost to a delivery failure isn't sent again on every read.
/// </summary>
public sealed class HitAndRunWatcher(
    IDbContextFactory<FetchshelfDbContext> contextFactory,
    Notifier notifier,
    TimeProvider timeProvider,
    ILogger<HitAndRunWatcher>? logger = null)
{
    private readonly ILogger<HitAndRunWatcher> _logger = logger ?? NullLogger<HitAndRunWatcher>.Instance;

    /// <summary>One check at a time: two reads close together must not both find the same rise.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Checks one account read. It never throws: it runs next to a job, which it must not fail.</summary>
    public async Task CheckAsync(MamAccountState account)
    {
        if (!notifier.Wants(NotificationEvent.HitAndRun)
            || account.SnatchKinds.FirstOrDefault(k => k.Key == AccountNotificationRules.HitAndRunSnatchKey) is not { } kind)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            var state = await context.NotificationStates.FirstOrDefaultAsync(s => s.Key == AccountNotificationRules.HitAndRunStateKey);
            long? stored = state is not null && long.TryParse(state.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;
            var (send, store) = AccountNotificationRules.DecideHitAndRun(stored, kind.Count);
            if (store is { } newCount)
            {
                var now = timeProvider.GetUtcNow();
                if (state is null)
                {
                    context.NotificationStates.Add(new NotificationStateEntity
                    {
                        Key = AccountNotificationRules.HitAndRunStateKey,
                        Value = newCount.ToString(CultureInfo.InvariantCulture),
                        UpdatedAt = now,
                    });
                }
                else
                {
                    (state.Value, state.UpdatedAt) = (newCount.ToString(CultureInfo.InvariantCulture), now);
                }

                await context.SaveChangesAsync();
            }

            if (send)
            {
                notifier.Publish(AccountNotificationRules.HitAndRun(kind.Count, timeProvider.GetUtcNow()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check the hit-and-run count: {Error}", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }
}
