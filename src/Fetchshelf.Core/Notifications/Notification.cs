namespace Fetchshelf.Core.Notifications;

/// <summary>One message to send, before a format turns it into a request.</summary>
/// <param name="Event">What it is about.</param>
/// <param name="Title">A short heading, such as "Job Link is failing".</param>
/// <param name="Message">The text.</param>
/// <param name="Time">When it happened.</param>
/// <param name="Fields">The event's data by name (MAM id, job name, counts), for the `json` format.</param>
/// <param name="LinkPath">
/// A page of Fetchshelf's web UI the message is about, such as <c>/torrents/abc</c>. It becomes a link when `web_url` is
/// set; without it, messages carry no links.
/// </param>
/// <param name="Item">
/// A short name for what the message is about, used when several of the same event are sent as one message
/// ("Grabbed 4 torrents: A, B, C and 1 more"). Only <see cref="NotificationEvent.Grabbed"/> and
/// <see cref="NotificationEvent.Linked"/> are merged.
/// </param>
/// <param name="SupersededBy">
/// An event that tells the same story: a target that wants it is not sent this one. A job's failure because MAM refused
/// `mam_id` is also a <see cref="NotificationEvent.MamRefused"/>, once, not once per job.
/// </param>
public sealed record Notification(
    NotificationEvent Event,
    string Title,
    string Message,
    DateTimeOffset Time,
    IReadOnlyDictionary<string, object?>? Fields = null,
    string? LinkPath = null,
    string? Item = null,
    NotificationEvent? SupersededBy = null);
