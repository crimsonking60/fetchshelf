namespace Fetchshelf.Data.Configuration;

/// <summary>The optional `[audiobookshelf]` section. Absent entirely (null
/// on <see cref="FetchshelfSettings.Audiobookshelf"/>) means the integration is off.</summary>
public sealed class AudiobookshelfSettings
{
    public string Url { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public int Interval { get; set; } = 10;

    /// <summary>
    /// Maps a path prefix as seen by Audiobookshelf to a path prefix as seen by Fetchshelf: the same order as
    /// qBittorrent's `path_mapping`.
    /// </summary>
    public Dictionary<string, string> PathMapping { get; set; } = [];

    /// <summary><see cref="PathMapping"/> turned around, for translating Fetchshelf's paths into Audiobookshelf's.</summary>
    public Dictionary<string, string> FetchshelfToAbsMapping() =>
        PathMapping.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
}
