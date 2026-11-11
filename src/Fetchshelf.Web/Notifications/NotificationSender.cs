using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Notifications;
using Microsoft.Extensions.Hosting;

namespace Fetchshelf.Web.Notifications;

/// <summary>
/// Takes notifications from the <see cref="Notifier"/>'s queue, merges bursts of grabs and links
/// (<see cref="NotificationBatcher"/>), and hands each message to <see cref="NotificationDelivery"/>. It runs apart from
/// the jobs, so a slow or unreachable service never holds one up.
/// </summary>
public sealed class NotificationSender(
    Notifier notifier, NotificationDelivery delivery, TimeProvider timeProvider, ILogger<NotificationSender> logger) : BackgroundService
{
    /// <summary>How long Fetchshelf stopping waits for what is still queued to be sent.</summary>
    public static readonly TimeSpan FlushOnStop = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batcher = new NotificationBatcher();
        var reader = notifier.Reader;
        Task<bool>? waiting = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // One read at a time stays pending across turns of the loop: the queue has a single reader.
                waiting ??= reader.WaitToReadAsync(stoppingToken).AsTask();
                using var turn = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var nextDue = batcher.NextDue;
                var wait = nextDue is { } due ? Task.Delay(Max(due - timeProvider.GetUtcNow(), TimeSpan.Zero), timeProvider, turn.Token) : Task.Delay(Timeout.Infinite, turn.Token);
                await Task.WhenAny(waiting, wait);
                await turn.CancelAsync();

                if (waiting.IsCompleted)
                {
                    if (!await waiting)
                    {
                        break;
                    }

                    waiting = null;
                    Drain(reader, batcher);
                }

                foreach (var notification in batcher.TakeReady(timeProvider.GetUtcNow()))
                {
                    await DeliverAsync(notification, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Fetchshelf is stopping.
        }

        await FlushAsync(reader, batcher);
    }

    private void Drain(System.Threading.Channels.ChannelReader<Notification> reader, NotificationBatcher batcher)
    {
        while (reader.TryRead(out var notification))
        {
            batcher.Add(notification, timeProvider.GetUtcNow());
        }
    }

    /// <summary>Sends what is queued or held, within <see cref="FlushOnStop"/>: a burst held back for 30 seconds mustn't be lost to a restart.</summary>
    private async Task FlushAsync(System.Threading.Channels.ChannelReader<Notification> reader, NotificationBatcher batcher)
    {
        using var limit = new CancellationTokenSource(FlushOnStop);
        try
        {
            Drain(reader, batcher);
            foreach (var notification in batcher.TakeReady(timeProvider.GetUtcNow(), everything: true))
            {
                await DeliverAsync(notification, limit.Token);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Some notifications could not be sent before Fetchshelf stopped");
        }
    }

    /// <summary>
    /// Delivery handles its own failures; this is for one nobody thought of. A background service that throws stops the
    /// host, and a notification must never take Fetchshelf down.
    /// </summary>
    private async Task DeliverAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await delivery.DeliverAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Could not send a {Event} notification: {Error}", NotificationEvents.Name(notification.Event), ex.Message);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
