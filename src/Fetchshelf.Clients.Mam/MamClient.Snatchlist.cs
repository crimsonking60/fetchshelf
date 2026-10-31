using System.Text.Json;

namespace Fetchshelf.Clients.Mam;

public sealed partial class MamClient
{
    /// <summary>
    /// Reads one page of a snatch list from `GET {CdnBaseUrl}/json/loadUserDetailsTorrents.php`,
    /// served from a separate CDN host (confirmed against the original Rust
    /// implementation's `mlm_mam::api::MaM::snatchlist`). `page` is 0-based; keep `cacheTimestamp`
    /// constant across a single multi-page listing.
    /// </summary>
    public async Task<IReadOnlyList<MamSnatchlistRow>> GetSnatchListAsync(
        MamSnatchlistKind kind, long page, DateTimeOffset cacheTimestamp, CancellationToken cancellationToken = default)
    {
        var account = await GetAccountStateAsync(cancellationToken);
        var url =
            $"{options.CdnBaseUrl.TrimEnd('/')}/json/loadUserDetailsTorrents.php" +
            $"?uid={account.Uid}&iteration={page}&type={kind.ToWireValue()}&cacheTime={cacheTimestamp.ToUnixTimeSeconds()}";

        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var rows = new List<MamSnatchlistRow>();
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            rows.Add(ParseRow(row));
        }

        return rows;
    }

    private static MamSnatchlistRow ParseRow(JsonElement row) => new()
    {
        MamId = MamJson.GetInt64(row.GetProperty("id")),
        Title = MamJson.GetString(row.GetProperty("title")),
        Seeders = MamJson.GetInt64(row.GetProperty("seeders")),
        Leechers = MamJson.GetInt64(row.GetProperty("leechers")),
        Finished = row.TryGetProperty("finished", out var finishedElement) && MamJson.GetBool(finishedElement),
        DownloadHash = row.TryGetProperty("dl", out var dlElement) && dlElement.ValueKind == JsonValueKind.String
            ? dlElement.GetString()
            : null,
    };
}
