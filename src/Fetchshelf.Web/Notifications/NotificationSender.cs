using System.Threading.Channels;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Microsoft.Extensions.Hosting;

namespace Fetchshelf.Web.Notifications;

/// <summary>
/// Takes notifications from the <see cref="Notifier"/>'s queue, merges bursts of grabs and links
/// (<see cref="NotificationBatcher"/>), and puts each message in the queue of every `[[notify]]` block that receives it.
/// Each block has a worker of its own, which sends its messages one after another: a service that is down, and takes
/// half a minute per message to give up on, holds up only its own messages, never another block's or a job.
/// </summary>
/// <remarks>
/// A message stays in its block's queue until it is being sent. When Fetchshelf stops, what is held is put in the
/// queues too, and the workers get <see cref="FlushOnStop"/> to send what is queued, a message already being sent
/// included. What is left after that is lost, and the log says how many.
/// </remarks>
public sealed class NotificationSender(
    Notifier notifier, NotificationDelivery delivery, TimeProvider timeProvider, ILogger<NotificationSender> logger) : BackgroundService
{
    /// <summary>How long Fetchshelf stopping waits for queued messages to be sent.</summary>
    public static readonly TimeSpan FlushOnStop = TimeSpan.FromSeconds(5);

    /// <summary>How many messages wait for one block; past that, its oldest is dropped.</summary>
    public const int QueuePerBlock = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sending goes on for FlushOnStop after Fetchshelf starts stopping; only then is a message cut off.
        using var grace = new CancellationTokenSource();
        using var graceOnStop = stoppingToken.Register(() => _ = EndGraceAsync(grace));

        var queues = delivery.Targets.ToDictionary(target => target, CreateQueue);
        var workers = queues.Select(pair => RunWorkerAsync(pair.Key, pair.Value.Reader, grace.Token)).ToList();
        var batcher = new NotificationBatcher();
        var reader = notifier.Reader;
        try
        {
            await RunAsync(reader, batcher, queues, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Fetchshelf is stopping.
        }

        // What is queued or held back goes to the blocks' queues, for the workers to send while the grace lasts.
        Drain(reader, batcher);
        Dispatch(batcher.TakeReady(timeProvider.GetUtcNow(), everything: true), queues);
        foreach (var queue in queues.Values)
        {
            queue.Writer.TryComplete();
        }

        var cutOff = (await Task.WhenAll(workers)).Sum();
        var lost = cutOff + queues.Values.Sum(queue => queue.Reader.Count);
        if (lost > 0)
        {
            logger.LogWarning("{Count} notifications could not be sent before Fetchshelf stopped", lost);
        }
    }

    /// <summary>Cancels <paramref name="grace"/> once <see cref="FlushOnStop"/> has passed, by the injected clock.</summary>
    private async Task EndGraceAsync(CancellationTokenSource grace)
    {
        await Task.Delay(FlushOnStop, timeProvider);
        try
        {
            await grace.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Everything was sent before the grace ran out.
        }
    }

    private async Task RunAsync(
        ChannelReader<Notification> reader, NotificationBatcher batcher, Dictionary<NotifyTarget, Channel<Notification>> queues, CancellationToken stoppingToken)
    {
        Task<bool>? waiting = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            // One read at a time stays pending across turns of the loop: the queue has a single reader.
            waiting ??= reader.WaitToReadAsync(stoppingToken).AsTask();
            using var turn = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var wait = batcher.NextDue is { } due
                ? Task.Delay(Max(due - timeProvider.GetUtcNow(), TimeSpan.Zero), timeProvider, turn.Token)
                : Task.Delay(Timeout.Infinite, turn.Token);
            await Task.WhenAny(waiting, wait);
            await turn.CancelAsync();

            if (waiting.IsCompleted)
            {
                if (!await waiting)
                {
                    return;
                }

                waiting = null;
                Drain(reader, batcher);
            }

            Dispatch(batcher.TakeReady(timeProvider.GetUtcNow()), queues);
        }
    }

    private Channel<Notification> CreateQueue(NotifyTarget target) => Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(QueuePerBlock) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true },
        dropped => logger.LogWarning(
            "Notify {Name}: too many messages waiting; dropped the oldest, {Event}: {Title}", target.Name, NotificationEvents.Name(dropped.Event), dropped.Title));

    private void Drain(ChannelReader<Notification> reader, NotificationBatcher batcher)
    {
        while (reader.TryRead(out var notification))
        {
            batcher.Add(notification, timeProvider.GetUtcNow());
        }
    }

    private static void Dispatch(IEnumerable<Notification> ready, Dictionary<NotifyTarget, Channel<Notification>> queues)
    {
        foreach (var notification in ready)
        {
            foreach (var (target, queue) in queues)
            {
                if (Notifier.Receives(target, notification))
                {
                    queue.Writer.TryWrite(notification);
                }
            }
        }
    }

    /// <summary>
    /// Sends one block's messages in order until its queue is completed and empty, or the grace runs out. Returns 1 when
    /// a message was cut off mid-send, else 0.
    /// </summary>
    private async Task<int> RunWorkerAsync(NotifyTarget target, ChannelReader<Notification> queue, CancellationToken cancellationToken)
    {
        // The message being sent: it has left the queue, so a cut-off one must be counted here.
        Notification? inFlight = null;
        try
        {
            while (await queue.WaitToReadAsync(cancellationToken))
            {
                while (queue.TryRead(out var notification))
                {
                    inFlight = notification;
                    await SendAsync(target, notification, cancellationToken);
                    inFlight = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return inFlight is null ? 0 : 1;
    }

    /// <summary>
    /// Delivery handles its own failures; this is for one nobody thought of. Logged without the exception, whose text
    /// may hold the block's URL.
    /// </summary>
    private async Task SendAsync(NotifyTarget target, Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await delivery.SendToAsync(target, notification, cancellationToken);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(
                "Notify {Name}: could not send {Event}: {Error}",
                target.Name, NotificationEvents.Name(notification.Event), NotificationDelivery.Redact(target, ex.Message));
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>Runs <see cref="HitAndRunWatcher"/>'s checks in the background, so MAM's account reads only hand them over.</summary>
public sealed class HitAndRunCheck(HitAndRunWatcher watcher) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => watcher.RunAsync(stoppingToken);
}
