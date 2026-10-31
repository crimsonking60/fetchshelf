using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.QBittorrent;

/// <summary>A torrent as reported by qBittorrent's `torrents/info`.</summary>
public sealed record QBittorrentTorrent
{
    [JsonPropertyName("hash")]
    public required string Hash { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("save_path")]
    public required string SavePath { get; init; }

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public string Tags { get; init; } = string.Empty;

    /// <summary>Fraction complete, 0.0-1.0 ("percentage/100" per the WebUI API).</summary>
    [JsonPropertyName("progress")]
    public double Progress { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    public IReadOnlyList<string> TagList => Tags.Length == 0
        ? []
        : Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A torrent is finished when, and only when, its progress is 100%.</summary>
    public bool IsFinished => Progress >= 1.0;
}
