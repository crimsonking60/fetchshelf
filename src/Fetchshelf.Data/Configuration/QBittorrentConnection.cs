namespace Fetchshelf.Data.Configuration;

/// <summary>One `[[qbittorrent]]` connection entry.</summary>
public sealed class QBittorrentConnection
{
    public string Url { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Maps a path prefix as seen by qBittorrent to a path prefix as seen by Fetchshelf.</summary>
    public Dictionary<string, string> PathMapping { get; set; } = [];

    public QBittorrentCategoryAndTags? OnCleaned { get; set; }

    public QBittorrentCategoryAndTags? OnUnlinked { get; set; }

    public QBittorrentCategoryAndTags? OnInvalidTorrent { get; set; }
}
