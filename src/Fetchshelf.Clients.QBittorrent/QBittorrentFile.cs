using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.QBittorrent;

/// <summary>A file belonging to a torrent, as reported by qBittorrent's `torrents/files`.</summary>
public sealed record QBittorrentFile
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }
}
