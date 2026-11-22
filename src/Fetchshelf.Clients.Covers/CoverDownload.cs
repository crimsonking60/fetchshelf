using System.Net;

namespace Fetchshelf.Clients.Covers;

/// <summary>Reading an image response: refusals, a missing image, and the size limit.</summary>
internal static class CoverDownload
{
    /// <summary>
    /// Throws <see cref="CoverServiceRefusedException"/> for 403, 429 and 5xx, which say nothing about the book; true for a
    /// success, false for any other answer (404 and the like: the service has nothing).
    /// </summary>
    public static bool Check(HttpResponseMessage response, string service)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
        {
            throw new CoverServiceRefusedException($"{service} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return response.IsSuccessStatusCode;
    }

    /// <summary>The response's body, or null when it is larger than <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
