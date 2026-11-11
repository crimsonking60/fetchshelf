namespace Fetchshelf.Core.Notifications;

/// <summary>Where code that notices something worth telling hands it over.</summary>
public interface INotificationSink
{
    /// <summary>
    /// Queues the notification and returns at once. It never waits on the network, and never throws: publishing can't
    /// fail the job that published.
    /// </summary>
    void Publish(Notification notification);

    /// <summary>
    /// Whether anyone wants the event, so code that must work to make the notification (read a torrent, compare a count)
    /// can skip the work when nobody does.
    /// </summary>
    bool Wants(NotificationEvent notificationEvent);
}
