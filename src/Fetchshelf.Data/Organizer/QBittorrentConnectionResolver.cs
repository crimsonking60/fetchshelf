using Fetchshelf.Clients.QBittorrent;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Resolves which configured qBittorrent connection currently knows a given hash, by probing each
/// one's hash-exists check in order, rather than persisting a connection identifier on the Torrent
/// record
/// </summary>
public static class QBittorrentConnectionResolver
{
    public static async Task<QBittorrentClient?> ResolveAsync(
        IReadOnlyList<QBittorrentClient> clients, string hash, CancellationToken cancellationToken = default)
    {
        foreach (var client in clients)
        {
            if (await client.HashExistsAsync(hash, cancellationToken))
            {
                return client;
            }
        }

        return null;
    }
}
