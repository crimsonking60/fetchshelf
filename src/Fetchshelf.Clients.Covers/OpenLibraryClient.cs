using System.Globalization;

namespace Fetchshelf.Clients.Covers;

/// <summary>
/// Open Library's cover service, which needs no key: a book's cover by its ISBN, the only thing sent. Requests are spaced
/// (<see cref="CoverClientOptions.OpenLibrarySpacing"/>), as its limits ask.
/// </summary>
public sealed class OpenLibraryClient
{
    private readonly HttpClient _httpClient;
    private readonly CoverClientOptions _options;
    private readonly RequestSpacer _spacer;

    public OpenLibraryClient(HttpClient httpClient, CoverClientOptions options, RequestSpacer? spacer = null)
    {
        _httpClient = httpClient;
        _options = options;
        _spacer = spacer ?? new RequestSpacer(options.OpenLibrarySpacing);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    /// <summary>
    /// The large cover of the book with this ISBN (10 or 13 digits), or null when Open Library has none (it answers 404,
    /// as <c>default=false</c> asks) or it is too large. Throws <see cref="CoverServiceRefusedException"/> when refused.
    /// </summary>
    public async Task<byte[]?> GetCoverByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        await _spacer.WaitTurnAsync(cancellationToken);
        var address = new Uri(_options.OpenLibraryBaseUrl, string.Create(CultureInfo.InvariantCulture, $"b/isbn/{Uri.EscapeDataString(isbn)}-L.jpg?default=false"));
        using var response = await _httpClient.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return CoverDownload.Check(response, "Open Library")
            ? await CoverDownload.ReadLimitedAsync(response, _options.MaxImageBytes, cancellationToken)
            : null;
    }
}
