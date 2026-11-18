namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Where a torrent's cover image is on MAM, from a search hit's <see cref="MamTorrentRecord.Thumbnail"/> or its poster
/// type: the Search page's detail view shows it, and the Link job writes it into book folders that have none.
/// </summary>
public static class MamCoverAddress
{
    /// <summary>The address of <paramref name="record"/>'s cover: its thumbnail when MAM sent one, else the poster on the CDN.</summary>
    public static string? For(MamTorrentRecord record, MamClientOptions options) =>
        ThumbnailUrl(record.Thumbnail, options.BaseUrl) ?? CoverUrl(record.MamId, record.PosterType, record.UploadedAt, options.CdnBaseUrl);

    /// <summary>The cover's address, made absolute against MAM when MAM sends a path; null for anything else.</summary>
    public static string? ThumbnailUrl(string? thumbnail, string mamBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnail))
        {
            return null;
        }

        // A path first: on Linux "/covers/a.jpg" would also pass as an absolute file:// address.
        if (thumbnail.StartsWith('/') && !thumbnail.StartsWith("//", StringComparison.Ordinal))
        {
            return $"{mamBaseUrl.TrimEnd('/')}{thumbnail}";
        }

        return Uri.TryCreate(thumbnail, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http"
            ? absolute.ToString()
            : null;
    }

    /// <summary>
    /// Where MAM keeps a torrent's cover: <c>{cdn}/t/p/{number}/large/{MAM id}.{type}</c>, with the type from
    /// <c>poster_type</c>. MAM's own pages put the cover's upload time in the number, but its CDN doesn't look at it;
    /// the torrent's upload time is used, so a changed cover still gets a new address. Null when the torrent has no
    /// cover, or one of a type this doesn't know.
    /// </summary>
    public static string? CoverUrl(long mamId, string? posterType, DateTimeOffset uploadedAt, string cdnBaseUrl)
    {
        var extension = posterType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpeg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => null,
        };
        return extension is null
            ? null
            : $"{cdnBaseUrl.TrimEnd('/')}/t/p/{uploadedAt.ToUnixTimeSeconds()}/large/{mamId}.{extension}";
    }
}
