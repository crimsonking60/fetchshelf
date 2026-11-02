using System.Text.Json;

namespace Fetchshelf.Clients.Mam;

public sealed partial class MamClient
{
    /// <summary>
    /// Reads one page of a snatch list from `GET {CdnBaseUrl}/json/loadUserDetailsTorrents.php`,
    /// served from a separate CDN host. `page` is 0-based; keep `cacheTimestamp`
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
        Details = ParseDetails(row),
    };

    /// <summary>
    /// The row's metadata fields. A row
    /// without `category` is treated as carrying none of them, so callers fall back to looking the torrent up.
    /// </summary>
    private static MamSnatchlistDetails? ParseDetails(JsonElement row)
    {
        if (!row.TryGetProperty("category", out var categoryElement))
        {
            return null;
        }

        return new MamSnatchlistDetails
        {
            LegacyCategoryId = (int)MamJson.GetInt64(categoryElement),
            GenreCategoryIds = IdNameArray(row, "categories").Select(item => (int)item.Id).ToList(),
            Authors = IdNameArray(row, "author").ToDictionary(item => item.Id, item => item.Name),
            Narrators = IdNameArray(row, "narrator").ToDictionary(item => item.Id, item => item.Name),
            Series = ParseSeries(row),
            FileTypes = IdNameArray(row, "fileTypes").Select(item => item.Name).ToList(),
            SizeRaw = OptionalString(row, "size"),
            TagsRaw = OptionalString(row, "tags"),
            FlagsRaw = row.TryGetProperty("browseFlags", out var flags) ? (int)MamJson.GetInt64(flags) : 0,
            IsFree = row.TryGetProperty("free", out var free) && MamJson.GetBool(free),
            IsPersonalFreeleech = row.TryGetProperty("personalFree", out var personalFree) && MamJson.GetBool(personalFree),
            IsVip = row.TryGetProperty("vip", out var vip) && MamJson.GetBool(vip),
            VipExpireUnixSeconds = row.TryGetProperty("vip_expire", out var vipExpire) && vipExpire.ValueKind != JsonValueKind.Null
                ? MamJson.GetInt64(vipExpire)
                : 0,
            UploaderId = row.TryGetProperty("uploaderID", out var uploaderId) ? MamJson.GetInt64(uploaderId) : 0,
            UploaderName = OptionalString(row, "uploaderName"),
            TimesCompleted = row.TryGetProperty("times_completed", out var completed) ? MamJson.GetInt64(completed) : 0,
        };
    }

    private static string OptionalString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? MamJson.GetString(element) : string.Empty;

    /// <summary>An array of `{ "id": …, "name": … }` objects, in id order; empty when the property is missing.</summary>
    private static IEnumerable<(long Id, string Name)> IdNameArray(JsonElement row, string name) =>
        row.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .Select(item => (Id: MamJson.GetInt64(item.GetProperty("id")), Name: MamJson.GetString(item.GetProperty("name"))))
                .OrderBy(item => item.Id)
                .ToList()
            : [];

    private static IReadOnlyList<MamSeriesInfo> ParseSeries(JsonElement row) =>
        row.TryGetProperty("series", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray()
                .OrderBy(item => MamJson.GetInt64(item.GetProperty("id")))
                .Select(item => new MamSeriesInfo(
                    MamJson.GetString(item.GetProperty("name")),
                    item.TryGetProperty("number", out var number) && number.ValueKind != JsonValueKind.Null ? MamJson.GetString(number) : string.Empty))
                .ToList()
            : [];
}
