namespace Fetchshelf.Clients.QBittorrent;

/// <summary>Connection details for one qBittorrent instance (Fetchshelf-spec.md §4.2).</summary>
public sealed record QBittorrentClientOptions(string BaseUrl, string Username, string Password);
