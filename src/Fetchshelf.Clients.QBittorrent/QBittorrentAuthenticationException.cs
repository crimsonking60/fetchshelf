namespace Fetchshelf.Clients.QBittorrent;

/// <summary>Thrown when logging in to, or re-authenticating with, a qBittorrent instance fails.</summary>
public sealed class QBittorrentAuthenticationException(string message) : Exception(message);
