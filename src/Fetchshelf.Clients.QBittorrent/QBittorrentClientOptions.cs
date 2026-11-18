namespace Fetchshelf.Clients.QBittorrent;

/// <summary>Connection details for one qBittorrent instance.</summary>
/// <param name="Name">The `name` of its `[[qbittorrent]]` block, which a block's `client` refers to it by; null when unnamed.</param>
public sealed record QBittorrentClientOptions(string BaseUrl, string Username, string Password, string? Name = null);
