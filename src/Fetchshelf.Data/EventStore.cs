using Fetchshelf.Core.Notifications;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data;

/// <summary>
/// Append-only access to the Event log. Deliberately exposes no update operation -
/// once written, an event's stored fields are never changed.
/// </summary>
/// <param name="dbContext">The context the event is saved in.</param>
/// <param name="notifications">Told of a grab, a link or a torrent MAM no longer has, once the event is saved; null for none.</param>
/// <param name="logger">Logs a notification that could not be made.</param>
public sealed class EventStore(FetchshelfDbContext dbContext, INotificationSink? notifications = null, ILogger<EventStore>? logger = null)
{
    private readonly ILogger<EventStore> _logger = logger ?? NullLogger<EventStore>.Instance;

    public async Task AppendAsync(EventEntity @event, CancellationToken cancellationToken = default)
    {
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync(cancellationToken);
        await PublishAsync(@event, cancellationToken);
    }

    /// <summary>
    /// Publishes the event's notification, and only after the event is saved: one that fails to save notifies nothing.
    /// A failure here is logged and goes no further: telling someone can't fail the job.
    /// </summary>
    private async Task PublishAsync(EventEntity @event, CancellationToken cancellationToken)
    {
        NotificationEvent? notificationEvent = @event.Kind switch
        {
            EventKind.Grabbed => NotificationEvent.Grabbed,
            EventKind.Linked => NotificationEvent.Linked,
            EventKind.RemovedFromMam => NotificationEvent.RemovedFromMam,
            _ => null,
        };

        // Reading the torrent costs a query per event: a run linking thousands of torrents doesn't pay it for nobody.
        if (notifications is null || notificationEvent is not { } wanted || !notifications.Wants(wanted))
        {
            return;
        }

        try
        {
            // The torrent is in this context already, or saved with the event: the title and authors come from it.
            var torrent = @event.TorrentId is { } id ? await dbContext.Torrents.FindAsync([id], cancellationToken) : null;
            var metadata = torrent is null ? null : TorrentMetadataSerializer.Deserialize(torrent.MetadataJson);
            if (EventNotifications.From(@event, metadata) is { } notification)
            {
                notifications.Publish(notification);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not make a notification for the {Kind} event: {Error}", @event.Kind, ex.Message);
        }
    }
}
