namespace Fetchshelf.Clients.Covers;

/// <summary>
/// Downloads a cover image from an address a cover service gave, with no credentials: only over HTTPS, only from the hosts
/// the caller allows, and at most <see cref="CoverClientOptions.MaxImageBytes"/>.
/// </summary>
public sealed class ImageClient
{
    private readonly HttpClient _httpClient;
    private readonly CoverClientOptions _options;

    public ImageClient(HttpClient httpClient, CoverClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    /// <summary>Whether the address is HTTPS on one of the hosts, or a subdomain of one.</summary>
    public static bool IsOnHost(Uri address, IEnumerable<string> hosts) =>
        address.Scheme == Uri.UriSchemeHttps
        && hosts.Any(host => address.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The image, or null when the address is elsewhere, the host has none, or it is too large. Throws
    /// <see cref="CoverServiceRefusedException"/> when the host refuses.
    /// </summary>
    public async Task<byte[]?> GetAsync(string? address, IEnumerable<string> hosts, string service, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !IsOnHost(uri, hosts))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return CoverDownload.Check(response, service)
            ? await CoverDownload.ReadLimitedAsync(response, _options.MaxImageBytes, cancellationToken)
            : null;
    }
}
