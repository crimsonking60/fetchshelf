namespace Fetchshelf.Core.Notifications;

/// <summary>How a `[[notify]]` block's messages are shaped: which service they are for.</summary>
public enum NotificationFormat
{
    Json,
    Ntfy,
    Gotify,
    Discord,
    Apprise,
}

/// <summary>The configuration's names for <see cref="NotificationFormat"/>.</summary>
public static class NotificationFormatNames
{
    private static readonly Dictionary<NotificationFormat, string> Names = new()
    {
        [NotificationFormat.Json] = "json",
        [NotificationFormat.Ntfy] = "ntfy",
        [NotificationFormat.Gotify] = "gotify",
        [NotificationFormat.Discord] = "discord",
        [NotificationFormat.Apprise] = "apprise",
    };

    public static string Name(NotificationFormat format) => Names[format];

    /// <summary>The format with this configuration name; false for an unknown one.</summary>
    public static bool TryParse(string name, out NotificationFormat format)
    {
        foreach (var (candidate, candidateName) in Names)
        {
            if (string.Equals(candidateName, name, StringComparison.Ordinal))
            {
                format = candidate;
                return true;
            }
        }

        format = default;
        return false;
    }

    public static string NameList => string.Join(", ", Names.Values);
}
