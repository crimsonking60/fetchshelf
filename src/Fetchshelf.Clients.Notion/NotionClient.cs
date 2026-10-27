using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Fetchshelf.Clients.Notion;

/// <summary>
/// Typed client for querying a Notion data source, Fetchshelf-spec.md §5.4. Unlike the MaM/Audiobookshelf
/// clients, the bearer token is per-`[[notion_list]]` config entry rather than a single app-wide
/// credential, so it is passed per call rather than set once on the <see cref="HttpClient"/>.
/// </summary>
public sealed class NotionClient(HttpClient httpClient)
{
    private const string ApiVersion = "2025-09-03";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<NotionDataSourceResponse> QueryDataSourceAsync(
        string dataSourceId, string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"https://api.notion.com/v1/data_sources/{Uri.EscapeDataString(dataSourceId)}/query")
        {
            Headers = { { "Notion-Version", ApiVersion } },
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NotionDataSourceResponse>(JsonOptions, cancellationToken);
        return result ?? new NotionDataSourceResponse();
    }
}
