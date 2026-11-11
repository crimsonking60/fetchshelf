using Fetchshelf.Core.Notifications;

namespace Fetchshelf.Data.Configuration;

/// <summary>One `[[notify]]` block: where messages go, what shape they take, and which events are sent.</summary>
public sealed class NotifyEntry
{
    /// <summary>A label for the log and the Config page; <c>notify[i]</c> when unset.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Where the message is POSTed. A secret: topics and webhook URLs often are one, so it is shown only as set. When
    /// <see cref="UrlFile"/> is set, <c>ConfigLoader</c> fills this from that file.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>Full path of a file holding the URL; an alternative to <see cref="Url"/>.</summary>
    public string? UrlFile { get; set; }

    /// <summary>`json`, `ntfy`, `gotify`, `discord` or `apprise`. Checked on load, so a mistake names the block.</summary>
    public string Format { get; set; } = "json";

    /// <summary>Sent as <c>Authorization: Bearer</c> when set.</summary>
    public string? Token { get; set; }

    /// <summary>Event names, or <c>all</c>. Checked on load.</summary>
    public List<string> Events { get; set; } = [];
}

/// <summary>A `[[notify]]` block as it is used: validated, with its URL read and its events resolved.</summary>
/// <param name="Name">The block's name, or <c>notify[i]</c>.</param>
/// <param name="Url">Where messages go. A secret: never logged or shown.</param>
/// <param name="Format">The service the messages are shaped for.</param>
/// <param name="Token">Sent as <c>Authorization: Bearer</c>; null for none.</param>
/// <param name="Events">The events this block wants.</param>
public sealed record NotifyTarget(string Name, string Url, NotificationFormat Format, string? Token, IReadOnlySet<NotificationEvent> Events)
{
    /// <summary>The targets of a loaded configuration, in order.</summary>
    public static IReadOnlyList<NotifyTarget> From(FetchshelfSettings settings) =>
        [.. settings.Notify.Select((entry, index) => new NotifyTarget(
            NameFor(entry, index),
            entry.Url!,
            NotificationFormatNames.TryParse(entry.Format, out var format) ? format : NotificationFormat.Json,
            string.IsNullOrWhiteSpace(entry.Token) ? null : entry.Token,
            WantedEvents(entry)))];

    /// <summary>The block's name: its own, or <c>notify[i]</c>.</summary>
    public static string NameFor(NotifyEntry entry, int index) => string.IsNullOrWhiteSpace(entry.Name) ? $"notify[{index}]" : entry.Name;

    /// <summary>The events the block lists; every event when it lists <c>all</c>. Names that aren't events are left out.</summary>
    public static IReadOnlySet<NotificationEvent> WantedEvents(NotifyEntry entry)
    {
        if (entry.Events.Contains(NotificationEvents.AllName, StringComparer.Ordinal))
        {
            return NotificationEvents.All.ToHashSet();
        }

        return entry.Events.Select(name => NotificationEvents.TryParse(name, out var e) ? (NotificationEvent?)e : null)
            .OfType<NotificationEvent>().ToHashSet();
    }
}
