using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fetchshelf.Core.Notifications;

/// <summary>An HTTP POST, described without sending it.</summary>
/// <param name="Url">Where it goes.</param>
/// <param name="Headers">The headers beyond the content type, in order.</param>
/// <param name="Body">The body, as text (sent as UTF-8).</param>
/// <param name="ContentType">The media type of the body.</param>
public sealed record NotificationRequest(string Url, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body, string ContentType);

/// <summary>
/// Turns a <see cref="Notification"/> into the request each service wants. Pure functions, so they are tested without a
/// network.
/// </summary>
public static class NotificationFormats
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // The body goes to a service, not into a web page: readable text beats escaped text.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string PlainText = "text/plain; charset=utf-8";
    private const string JsonType = "application/json";

    /// <param name="format">Which service the message is for.</param>
    /// <param name="notification">The message.</param>
    /// <param name="url">The block's URL.</param>
    /// <param name="token">Sent as <c>Authorization: Bearer</c> when set.</param>
    /// <param name="webUrl">The address of Fetchshelf's web UI, which the message links to; null for no links.</param>
    public static NotificationRequest Build(NotificationFormat format, Notification notification, string url, string? token, string? webUrl)
    {
        var link = Link(notification, webUrl);
        var request = format switch
        {
            NotificationFormat.Ntfy => Ntfy(notification, url, link),
            NotificationFormat.Gotify => Gotify(notification, url, link),
            NotificationFormat.Discord => Discord(notification, url, link),
            NotificationFormat.Apprise => Apprise(notification, url, link),
            _ => Json(notification, url, link),
        };

        return string.IsNullOrEmpty(token)
            ? request
            : request with { Headers = [new("Authorization", $"Bearer {token}"), .. request.Headers] };
    }

    /// <summary>The link a message carries: the web address and the page it is about; null without a web address.</summary>
    public static string? Link(Notification notification, string? webUrl) =>
        string.IsNullOrWhiteSpace(webUrl) || notification.LinkPath is null
            ? null
            : webUrl.TrimEnd('/') + (notification.LinkPath.StartsWith('/') ? notification.LinkPath : "/" + notification.LinkPath);

    private static NotificationRequest Json(Notification notification, string url, string? link)
    {
        var body = new JsonObject
        {
            ["event"] = NotificationEvents.Name(notification.Event),
            ["title"] = notification.Title,
            ["message"] = notification.Message,
        };
        if (link is not null)
        {
            body["url"] = link;
        }

        body["time"] = Time(notification);
        body["fields"] = notification.Fields is null ? new JsonObject() : JsonSerializer.SerializeToNode(notification.Fields, JsonOptions);
        return new NotificationRequest(url, [], body.ToJsonString(JsonOptions), JsonType);
    }

    private static NotificationRequest Ntfy(Notification notification, string url, string? link)
    {
        List<KeyValuePair<string, string>> headers =
        [
            new("Title", EncodeHeader(notification.Title)),
            new("Tags", NtfyTag(notification.Event)),
        ];
        if (link is not null)
        {
            headers.Add(new("Click", link));
        }

        if (NotificationEvents.IsUrgent(notification.Event))
        {
            headers.Add(new("Priority", "high"));
        }

        return new NotificationRequest(url, headers, notification.Message, PlainText);
    }

    private static string NtfyTag(NotificationEvent notificationEvent) => notificationEvent switch
    {
        _ when NotificationEvents.IsProblem(notificationEvent) => "warning",
        NotificationEvent.Grabbed or NotificationEvent.Linked => "books",
        NotificationEvent.JobRecovered => "white_check_mark",
        _ => "information_source",
    };

    private static NotificationRequest Gotify(Notification notification, string url, string? link)
    {
        var body = new JsonObject
        {
            ["title"] = notification.Title,
            ["message"] = notification.Message,
            ["priority"] = NotificationEvents.IsProblem(notification.Event) ? 8 : 4,
        };
        if (link is not null)
        {
            body["extras"] = new JsonObject
            {
                ["client::notification"] = new JsonObject { ["click"] = new JsonObject { ["url"] = link } },
            };
        }

        return new NotificationRequest(url, [], body.ToJsonString(JsonOptions), JsonType);
    }

    private static NotificationRequest Discord(Notification notification, string url, string? link)
    {
        const int titleLimit = 256;
        const int descriptionLimit = 4_000;
        var embed = new JsonObject
        {
            ["title"] = Truncate(notification.Title, titleLimit),
            ["description"] = Truncate(notification.Message, descriptionLimit),
        };
        if (link is not null)
        {
            embed["url"] = link;
        }

        embed["color"] = DiscordColor(notification.Event);

        // An explicit null content says the embed is the message; Discord needs content or an embed.
        var body = new JsonObject { ["content"] = null, ["embeds"] = new JsonArray(embed) };
        return new NotificationRequest(url, [], body.ToJsonString(JsonOptions), JsonType);
    }

    private static int DiscordColor(NotificationEvent notificationEvent) => notificationEvent switch
    {
        _ when NotificationEvents.IsProblem(notificationEvent) => 0xE74C3C, // red
        NotificationEvent.JobRecovered => 0x2ECC71, // green
        _ => 0x3498DB, // blue
    };

    private static NotificationRequest Apprise(Notification notification, string url, string? link)
    {
        var body = new JsonObject
        {
            ["title"] = notification.Title,
            // Apprise has no field for a link, so it goes at the end of the text.
            ["body"] = link is null ? notification.Message : $"{notification.Message}\n{link}",
            ["type"] = AppriseType(notification.Event),
        };
        return new NotificationRequest(url, [], body.ToJsonString(JsonOptions), JsonType);
    }

    private static string AppriseType(NotificationEvent notificationEvent) => notificationEvent switch
    {
        NotificationEvent.MamRefused or NotificationEvent.JobFailing => "failure",
        _ when NotificationEvents.IsProblem(notificationEvent) => "warning",
        NotificationEvent.JobRecovered => "success",
        _ => "info",
    };

    private static string Time(Notification notification) => notification.Time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string Truncate(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";

    /// <summary>
    /// A header value as HTTP can carry it: printable ASCII as it is, anything else as an RFC 2047 encoded word, which
    /// ntfy decodes. Titles hold job names and book titles, which can be in any script.
    /// </summary>
    public static string EncodeHeader(string value)
    {
        var singleLine = value.ReplaceLineEndings(" ");
        return singleLine.All(c => c is >= ' ' and <= '~')
            ? singleLine
            : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(singleLine))}?=";
    }
}
