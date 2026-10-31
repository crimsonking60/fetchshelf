namespace Fetchshelf.Clients.QBittorrent;

/// <summary>Connection details for one qBittorrent instance.</summary>
public sealed record QBittorrentClientOptions(string BaseUrl, string Username, string Password);
