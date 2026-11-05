namespace Fetchshelf.Clients.Mam;

/// <summary>An image MAM served, such as a torrent's cover.</summary>
public sealed record MamImage(byte[] Bytes, string ContentType);

public sealed partial class MamClient
{
    /// <summary>Covers larger than this are not passed on.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Whether <paramref name="url"/> is an https address on MAM itself: the site, its CDN, or another host under MAM's
    /// domain. Only such addresses are fetched with the session cookie, so an address taken from MAM's answer can never
    /// send it, or a request, anywhere else.
    /// </summary>
    public bool IsMamAddress(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var siteHost = new Uri(options.BaseUrl).Host;
        var domain = siteHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? siteHost[4..] : siteHost;
        return uri.Host.Equals(siteHost, StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals(new Uri(options.CdnBaseUrl).Host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches an image from MAM with the session cookie, for showing a cover to a browser that may not be logged in to
    /// MAM. Null when the address is not on MAM, or the answer is not an image or is larger than
    /// <see cref="MaxImageBytes"/>.
    /// </summary>
    public async Task<MamImage?> DownloadImageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsMamAddress(url))
        {
            return null;
        }

        // A refused cover says nothing about the session, so it doesn't pause other MAM calls.
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken, pauseOnForbidden: false);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode || contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || response.Content.Headers.ContentLength > MaxImageBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return new MamImage(buffer.ToArray(), contentType);
    }
}
