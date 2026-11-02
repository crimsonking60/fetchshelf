using System.Text.Json;
using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// Turns a failed MAM call into one plain sentence for the UI. Status codes and the exceptions' own messages are for the
/// log, which the caller writes.
/// </summary>
public static class MamErrorDescriber
{
    public static string Describe(Exception exception, TimeSpan timeout) => exception switch
    {
        // HttpClient reports its own Timeout as a TaskCanceledException; a request the browser abandoned is rethrown by the caller before getting here.
        OperationCanceledException or TimeoutException =>
            $"MAM did not answer within {timeout.TotalSeconds:0} seconds. It may be slow or down - try again in a moment.",
        MamRateLimitedException => "MAM is limiting requests from this account. Wait a few minutes and try again.",
        MamAccountBlockedException => "MAM has temporarily blocked this account. Wait a while before trying again.",
        // Already says why, what to do, and until when MAM calls are paused.
        MamForbiddenException => exception.Message,
        HttpRequestException { StatusCode: { } } => "MAM answered with an error. It may be down - try again later.",
        HttpRequestException => "MAM could not be reached. Check this machine's internet connection.",
        JsonException => "MAM sent a response Fetchshelf could not read.",
        _ => "Something went wrong while talking to MAM. The log has the details.",
    };

    /// <summary>
    /// For an action that talks to MAM and other services: only MAM's own refusals can be told apart by type, so anything
    /// else is put down to the services as a group, named in <paramref name="services"/> (e.g. "MAM or qBittorrent").
    /// </summary>
    public static string DescribeAmong(Exception exception, string services) => exception switch
    {
        MamRateLimitedException or MamAccountBlockedException or MamForbiddenException =>
            Describe(exception, TimeSpan.Zero),
        OperationCanceledException or TimeoutException => $"{services} did not answer in time. Try again in a moment.",
        _ => $"{services} could not be reached or answered with an error. The log has the details.",
    };
}
