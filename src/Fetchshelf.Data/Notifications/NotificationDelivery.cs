using System.Net.Http.Headers;
using System.Text;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Notifications;

/// <summary>What sending one message to one block came to.</summary>
/// <param name="Succeeded">Whether the service accepted it.</param>
/// <param name="Status">The HTTP status it answered with; null when it never answered.</param>
/// <param name="Error">What went wrong, without the URL, which is a secret; null on success.</param>
public sealed record DeliveryResult(bool Succeeded, int? Status, string? Error)
{
    /// <summary>"HTTP 200", or the error.</summary>
    public string Describe() => Error ?? (Status is { } status ? $"HTTP {status}" : "no answer");
}

/// <summary>
/// Sends notifications to the `[[notify]]` blocks that receive them, one HTTP POST each. A failure, a 5xx or a 429 is
/// tried once more after <see cref="RetryAfter"/>; then the message is dropped and logged as a warning. Delivery failures
/// are not themselves notified.
/// </summary>
/// <param name="targets">Every `[[notify]]` block.</param>
/// <param name="createClient">Makes the HTTP client to use; it carries the 10-second timeout.</param>
/// <param name="webUrl">The address of Fetchshelf's web UI, which messages link to; null for no links.</param>
/// <param name="logger">Logs a dropped message.</param>
/// <param name="delay">Waits before a retry; replaced in tests.</param>
public sealed class NotificationDelivery(
    IReadOnlyList<NotifyTarget> targets,
    Func<HttpClient> createClient,
    string? webUrl,
    ILogger<NotificationDelivery>? logger = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>How long after a failed attempt the message is sent once more.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly ILogger<NotificationDelivery> _logger = logger ?? NullLogger<NotificationDelivery>.Instance;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, ct));

    /// <summary>Sends the notification to every block that receives it, all at once, so one slow service holds up no other.</summary>
    public Task DeliverAsync(Notification notification, CancellationToken cancellationToken = default) =>
        Task.WhenAll(targets.Where(target => Notifier.Receives(target, notification)).Select(target => DeliverToAsync(target, notification, cancellationToken)));

    private async Task DeliverToAsync(NotifyTarget target, Notification notification, CancellationToken cancellationToken)
    {
        var result = await SendAsync(target, notification, retry: true, cancellationToken);
        if (!result.Succeeded && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Notify {Name}: could not send {Event}: {Error}", target.Name, NotificationEvents.Name(notification.Event), result.Describe());
        }
    }

    /// <summary>
    /// Sends the Config page's test message to one block, once and at once, so the page can show the answer. It doesn't
    /// look at which events the block wants.
    /// </summary>
    public Task<DeliveryResult> SendTestAsync(NotifyTarget target, CancellationToken cancellationToken = default)
    {
        var notification = new Notification(
            NotificationEvent.Grabbed,
            "Fetchshelf test message",
            $"Fetchshelf test message from {Environment.MachineName}",
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["test"] = true },
            "/Config");
        return SendAsync(target, notification, retry: false, cancellationToken);
    }

    private async Task<DeliveryResult> SendAsync(NotifyTarget target, Notification notification, bool retry, CancellationToken cancellationToken)
    {
        var request = NotificationFormats.Build(target.Format, notification, target.Url, target.Token, webUrl);
        var result = await TryOnceAsync(target, request, cancellationToken);
        if (result.Succeeded || !retry || !IsWorthRetrying(result) || cancellationToken.IsCancellationRequested)
        {
            return result;
        }

        try
        {
            await _delay(RetryAfter, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return result;
        }

        return await TryOnceAsync(target, request, cancellationToken);
    }

    /// <summary>A failure to connect, a server error and a rate limit may pass; a refused token or a wrong address won't.</summary>
    private static bool IsWorthRetrying(DeliveryResult result) => result.Status is null or >= 500 or 429;

    private async Task<DeliveryResult> TryOnceAsync(NotifyTarget target, NotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, request.Url);
            foreach (var (name, value) in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }

            message.Content = new StringContent(request.Body, Encoding.UTF8);
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            using var response = await createClient().SendAsync(message, cancellationToken);
            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode ? new DeliveryResult(true, status, null) : new DeliveryResult(false, status, $"HTTP {status}");
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Whatever it was, it is this message's failure: it must not reach the sender, whose crash would stop the host.
            return new DeliveryResult(false, null, Describe(ex, target.Url, cancellationToken));
        }
    }

    /// <summary>The cause in words, and never the URL: an address with a topic or a webhook id in it is a secret.</summary>
    private static string Describe(Exception ex, string url, CancellationToken cancellationToken) =>
        ex is TaskCanceledException && !cancellationToken.IsCancellationRequested
            ? "no answer within the time limit"
            : ex.Message.Replace(url, "(url)", StringComparison.Ordinal);
}
