using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fetchshelf.Clients.QBittorrent;

/// <summary>A torrent as reported by qBittorrent's `torrents/info`.</summary>
public sealed partial record QBittorrentTorrent
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

    /// <summary>The .torrent file's comment, which qBittorrent 5 reports. MAM writes "MID=&lt;torrent id&gt;,UID=&lt;user id&gt;" there.</summary>
    /// <summary>Seconds spent seeding after it completed, in total.</summary>
    [JsonPropertyName("seeding_time")]
    public long SeedingTime { get; init; }

    /// <summary>When qBittorrent added it, as Unix seconds.</summary>
    [JsonPropertyName("added_on")]
    public long AddedOn { get; init; }

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;

    /// <summary>The tracker qBittorrent last reached for it; empty when none answered.</summary>
    [JsonPropertyName("tracker")]
    public string Tracker { get; init; } = string.Empty;

    /// <summary>
    /// A MAM torrent: its tracker is MAM's, or, when no tracker has answered yet, its comment names a MAM torrent id, as
    /// MAM's torrent files do.
    /// </summary>
    public bool IsMam => Uri.TryCreate(Tracker, UriKind.Absolute, out var tracker)
        ? tracker.Host.Equals("myanonamouse.net", StringComparison.OrdinalIgnoreCase)
            || tracker.Host.EndsWith(".myanonamouse.net", StringComparison.OrdinalIgnoreCase)
        : MamId is not null;

    /// <summary>The MAM torrent id from <see cref="Comment"/>, or null when the comment has none.</summary>
    public long? MamId => MamIdPattern().Match(Comment) is { Success: true } match
        && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public IReadOnlyList<string> TagList => Tags.Length == 0
        ? []
        : Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A torrent is finished when, and only when, its progress is 100%.</summary>
    public bool IsFinished => Progress >= 1.0;

    /// <summary>
    /// qBittorrent can't find the torrent's files where it keeps them (state <c>missingFiles</c>): moved or deleted outside
    /// it. Its progress then reads 0, though it may have finished long ago.
    /// </summary>
    public bool HasMissingFiles => State == "missingFiles";

    /// <summary>What to do about <see cref="HasMissingFiles"/>, naming where qBittorrent looks for them.</summary>
    public string MissingFilesAdvice =>
        $"qBittorrent can't find its files in {SavePath} (Missing files): move them back there, or use Set location in qBittorrent, then Force recheck.";

    [GeneratedRegex(@"\bMID=(\d+)")]
    private static partial Regex MamIdPattern();
}
