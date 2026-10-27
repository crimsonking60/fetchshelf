using System.Text.Json;
using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Web.Pages;

/// <summary>Turns a failed MaM call into one plain sentence for the UI (the exception types' own messages are for the log).</summary>
public static class MamErrorDescriber
{
    public static string Describe(Exception exception, TimeSpan timeout) => exception switch
    {
        // HttpClient reports its own Timeout as a TaskCanceledException; a request the browser abandoned is rethrown by the caller before getting here.
        OperationCanceledException or TimeoutException =>
            $"MaM did not answer within {timeout.TotalSeconds:0} seconds. It may be slow or down - try again in a moment.",
        MamAuthenticationException or MamRateLimitedException or MamAccountBlockedException or MamForbiddenException => exception.Message,
        HttpRequestException { StatusCode: { } status } => $"MaM answered with HTTP {(int)status} ({status}).",
        HttpRequestException http => $"MaM could not be reached ({http.InnerException?.Message ?? http.Message}).",
        JsonException => "MaM sent a response Fetchshelf could not read.",
        _ => $"Unexpected error: {exception.Message}",
    };
}
