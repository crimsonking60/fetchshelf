using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Clients.Abs;

/// <summary>
/// Typed client for the Audiobookshelf API. Every request is bearer-authenticated
/// with the configured token.
/// </summary>
public sealed class AbsClient
{
    private readonly HttpClient _httpClient;
    private readonly AbsClientOptions _options;

    public AbsClient(HttpClient httpClient, AbsClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
    }

    public async Task<IReadOnlyList<AbsLibrary>> ListLibrariesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(BuildUrl("/api/libraries"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AbsLibrariesResponse>(cancellationToken: cancellationToken);
        return result?.Libraries ?? [];
    }

    public async Task<IReadOnlyList<AbsAuthor>> SearchLibraryAsync(
        string libraryId, string authorName, CancellationToken cancellationToken = default)
    {
        var url = $"{BuildUrl($"/api/libraries/{Uri.EscapeDataString(libraryId)}/search")}?q={Uri.EscapeDataString(authorName)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AbsSearchResponse>(cancellationToken: cancellationToken);
        return result?.Authors ?? [];
    }

    public async Task<IReadOnlyList<AbsLibraryItem>> GetAuthorItemsAsync(string authorId, CancellationToken cancellationToken = default)
    {
        var url = $"{BuildUrl($"/api/authors/{Uri.EscapeDataString(authorId)}")}?include=items";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AbsAuthorResponse>(cancellationToken: cancellationToken);
        return result?.LibraryItems ?? [];
    }

    public async Task UpdateItemMetadataAsync(string itemId, TorrentMetadata metadata, CancellationToken cancellationToken = default)
    {
        var request = new AbsMetadataUpdateRequest { Metadata = ToAbsMetadata(metadata) };
        using var response = await _httpClient.PatchAsync(
            BuildUrl($"/api/items/{Uri.EscapeDataString(itemId)}/media"), JsonContent.Create(request), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync(BuildUrl($"/api/items/{Uri.EscapeDataString(itemId)}"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static AbsMetadata ToAbsMetadata(TorrentMetadata metadata)
    {
        var (title, subtitle) = LibraryMetadataJsonBuilder.SplitTitle(metadata.Title);

        return new AbsMetadata
        {
            Title = title,
            Subtitle = subtitle,
            Authors = metadata.Authors.Select(name => new AbsAuthorName(name)).ToList(),
            Narrators = metadata.Narrators,
            Series = metadata.Series
                .Select(series => new AbsSeries { Name = series.Name, Sequence = LibraryMetadataJsonBuilder.FormatSequence(series) })
                .ToList(),
            Description = metadata.Description,
            Isbn = metadata.Isbn,
            Asin = metadata.Asin,
            Genres = LibraryMetadataJsonBuilder.BuildGenres(metadata),
            Language = metadata.Language.ToString(),
            Explicit = metadata.Flags.HasFlag(BookFlags.Explicit),
            Abridged = metadata.Flags.HasFlag(BookFlags.Abridged),
        };
    }

    private string BuildUrl(string path) => $"{_options.BaseUrl.TrimEnd('/')}{path}";
}
