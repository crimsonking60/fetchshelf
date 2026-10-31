using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Fetchshelf.Clients.Notion;

/// <summary>
/// Typed client for querying a Notion data source. Unlike the MAM/Audiobookshelf
/// clients, the bearer token is per-`[[notion_list]]` config entry rather than a single app-wide
/// credential, so it is passed per call rather than set once on the <see cref="HttpClient"/>.
/// </summary>
public sealed class NotionClient(HttpClient httpClient)
{
    private const string ApiVersion = "2025-09-03";

    /// <summary>The largest page Notion's query endpoint returns.</summary>
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Every row of the data source, following Notion's paging until the last page.</summary>
    public async Task<IReadOnlyList<NotionRow>> QueryAllRowsAsync(
        string dataSourceId, string token, CancellationToken cancellationToken = default)
    {
        var rows = new List<NotionRow>();
        string? cursor = null;
        do
        {
            var page = await QueryPageAsync(dataSourceId, token, cursor, cancellationToken);
            rows.AddRange(page.Results);
            cursor = page.HasMore ? page.NextCursor : null;
        }
        while (cursor is not null);

        return rows;
    }

    private async Task<NotionDataSourceResponse> QueryPageAsync(
        string dataSourceId, string token, string? startCursor, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"https://api.notion.com/v1/data_sources/{Uri.EscapeDataString(dataSourceId)}/query")
        {
            Headers = { { "Notion-Version", ApiVersion } },
            Content = new StringContent(
                JsonSerializer.Serialize(new QueryRequest(PageSize, startCursor), JsonOptions), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NotionDataSourceResponse>(JsonOptions, cancellationToken);
        return result ?? new NotionDataSourceResponse();
    }

    private sealed record QueryRequest(int PageSize, string? StartCursor);
}
