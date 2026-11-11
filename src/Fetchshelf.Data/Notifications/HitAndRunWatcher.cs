using System.Threading.Channels;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Notifications;

/// <summary>
/// Compares each account read with the last hit-and-run count seen, which is kept in the database, so one that appears
/// while Fetchshelf is down is still sent after the restart. The count is only compared and stored while some
/// `[[notify]]` block wants <see cref="NotificationEvent.HitAndRun"/>. It is stored when the notification is queued, not
/// when it is delivered: a message lost to a delivery failure isn't sent again on every read.
/// </summary>
/// <remarks>
/// <see cref="Enqueue"/> is what MAM's account reads call: it only hands the read over, so the job that read the account
/// never waits on the database. <see cref="RunAsync"/>, a background loop, does the comparing. Only the latest read waits:
/// an older one says nothing a newer one doesn't.
/// </remarks>
public sealed class HitAndRunWatcher(NotificationStateStore states, INotificationSink notifications, TimeProvider timeProvider, ILogger<HitAndRunWatcher>? logger = null)
{
    private readonly ILogger<HitAndRunWatcher> _logger = logger ?? NullLogger<HitAndRunWatcher>.Instance;

    private readonly Channel<MamAccountState> _reads = Channel.CreateBounded<MamAccountState>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    /// <summary>One check at a time: two reads close together must not both find the same rise.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Hands an account read over for checking, and returns at once.</summary>
    public void Enqueue(MamAccountState account)
    {
        if (notifications.Wants(NotificationEvent.HitAndRun))
        {
            _reads.Writer.TryWrite(account);
        }
    }

    /// <summary>Checks each read handed over, until <paramref name="stoppingToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var account in _reads.Reader.ReadAllAsync(stoppingToken))
            {
                await CheckAsync(account);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Fetchshelf is stopping: a read not yet checked is checked against the stored count on the next read.
        }
    }

    /// <summary>Checks one account read. It never throws.</summary>
    public async Task CheckAsync(MamAccountState account)
    {
        if (!notifications.Wants(NotificationEvent.HitAndRun)
            || account.SnatchKinds.FirstOrDefault(k => k.Key == AccountNotificationRules.HitAndRunSnatchKey) is not { } kind)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var stored = await states.GetAsync(AccountNotificationRules.HitAndRunStateKey);
            var (send, store) = AccountNotificationRules.DecideHitAndRun(stored, kind.Count);
            if (store is { } newCount)
            {
                await states.SetAsync(AccountNotificationRules.HitAndRunStateKey, newCount);
            }

            if (send)
            {
                notifications.Publish(AccountNotificationRules.HitAndRun(kind.Count, timeProvider.GetUtcNow()));
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
