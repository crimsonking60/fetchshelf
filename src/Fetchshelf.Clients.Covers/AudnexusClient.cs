using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Covers;

/// <summary>
/// Audnexus, which serves Audible's details of an audiobook and needs no key: its cover by its ASIN, the only thing sent.
/// The image itself comes from Amazon's image host, and from nowhere else. Requests to Audnexus are spaced
/// (<see cref="CoverClientOptions.AudnexusSpacing"/>).
/// </summary>
public sealed class AudnexusClient
{
    /// <summary>The hosts Audible's covers are served from; an image address elsewhere isn't followed.</summary>
    private static readonly string[] ImageHosts = ["media-amazon.com", "ssl-images-amazon.com", "images-amazon.com"];

    private readonly HttpClient _httpClient;
    private readonly CoverClientOptions _options;
    private readonly RequestSpacer _spacer;

    public AudnexusClient(HttpClient httpClient, CoverClientOptions options, RequestSpacer? spacer = null)
    {
        _httpClient = httpClient;
        _options = options;
        _spacer = spacer ?? new RequestSpacer(options.AudnexusSpacing);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    /// <summary>
    /// The cover of the audiobook with this ASIN, or null when Audnexus doesn't know it, has no image, gives one elsewhere
    /// than Amazon's image host, or one too large. Throws <see cref="CoverServiceRefusedException"/> when refused.
    /// </summary>
    public async Task<byte[]?> GetCoverByAsinAsync(string asin, CancellationToken cancellationToken = default)
    {
        await _spacer.WaitTurnAsync(cancellationToken);
        AudnexusBook? book;
        using (var response = await _httpClient.GetAsync(new Uri(_options.AudnexusBaseUrl, $"books/{Uri.EscapeDataString(asin)}"), cancellationToken))
        {
            if (!CoverDownload.Check(response, "Audnexus"))
            {
                return null;
            }

            book = await response.Content.ReadFromJsonAsync<AudnexusBook>(cancellationToken);
        }

        if (!Uri.TryCreate(book?.Image, UriKind.Absolute, out var image) || !IsImageHost(image))
        {
            return null;
        }

        using var imageResponse = await _httpClient.GetAsync(image, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return CoverDownload.Check(imageResponse, "Amazon's image host")
            ? await CoverDownload.ReadLimitedAsync(imageResponse, _options.MaxImageBytes, cancellationToken)
            : null;
    }

    /// <summary>Whether the address is on Amazon's image host, over HTTPS.</summary>
    public static bool IsImageHost(Uri address) => ImageClient.IsOnHost(address, ImageHosts);

    private sealed record AudnexusBook([property: JsonPropertyName("image")] string? Image);
}
