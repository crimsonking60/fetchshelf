using System.Threading.Channels;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Notifications;

/// <summary>
/// Where notifications are published. <see cref="Publish"/> only puts a notification in a bounded in-memory queue, so
/// publishing never waits on the network and never fails the job that published; the sender takes from the queue. A
/// notification no `[[notify]]` block would receive isn't queued at all. Notifications are not stored: one lost in a
/// restart is lost, and the Events page and the job history hold the same facts.
/// </summary>
public sealed class Notifier : INotificationSink
{
    /// <summary>How many notifications wait for the sender; past that, the oldest is dropped.</summary>
    public const int QueueSize = 200;

    private readonly ILogger<Notifier> _logger;
    private readonly Channel<Notification> _queue;

    public Notifier(IReadOnlyList<NotifyTarget> targets, ILogger<Notifier>? logger = null)
    {
        Targets = targets;
        _logger = logger ?? NullLogger<Notifier>.Instance;
        _queue = Channel.CreateBounded<Notification>(
            new BoundedChannelOptions(QueueSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped => _logger.LogWarning(
                "The notification queue is full; dropped the oldest message, {Event}: {Title}", NotificationEvents.Name(dropped.Event), dropped.Title));
    }

    /// <summary>Every `[[notify]]` block, in order.</summary>
    public IReadOnlyList<NotifyTarget> Targets { get; }

    /// <summary>What the sender reads.</summary>
    public ChannelReader<Notification> Reader => _queue.Reader;

    /// <summary>Whether any block wants the event, so code that must work to find one can skip the work when none does.</summary>
    public bool Wants(NotificationEvent notificationEvent) => Targets.Any(target => target.Events.Contains(notificationEvent));

    /// <summary>Whether the block gets this notification: it wants the event, and not an event that tells the same story.</summary>
    public static bool Receives(NotifyTarget target, Notification notification) =>
        target.Events.Contains(notification.Event)
        && !(notification.SupersededBy is { } superseding && target.Events.Contains(superseding));

    public void Publish(Notification notification)
    {
        try
        {
            if (Targets.Any(target => Receives(target, notification)))
            {
                _queue.Writer.TryWrite(notification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not queue a notification: {Error}", ex.Message);
        }
    }
}
