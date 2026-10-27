using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <summary>Client status values Fetchshelf sets itself, alongside qBittorrent's own state strings.</summary>
public static class ClientStatuses
{
    public const string NotInClient = "not in client";
    public const string RemovedFromMam = "removed from MaM";
}

/// <param name="Checked">Tracked torrents looked at.</param>
/// <param name="FoundInClient">Of those, how many qBittorrent reported (their status and category were refreshed).</param>
/// <param name="NotInClient">Of those, how many no qBittorrent instance has; always 0 when a connection failed.</param>
/// <param name="Errors">One entry per connection that could not be queried, or a single entry if none is configured.</param>
public sealed record ClientStatusCheckResult(int Checked, int FoundInClient, int NotInClient, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Refreshes the stored client status of every tracked torrent from the configured qBittorrent instances, on demand.
/// The Link job only updates finished torrents it processes; this also covers torrents that are still downloading,
/// stopped, or gone from the client.
/// </summary>
public sealed class ClientStatusChecker(FetchshelfDbContext dbContext)
{
    public async Task<ClientStatusCheckResult> CheckAsync(IReadOnlyList<QBittorrentClient> clients, CancellationToken cancellationToken = default)
    {
        var tracked = await dbContext.Torrents.ToListAsync(cancellationToken);
        if (clients.Count == 0)
        {
            return new ClientStatusCheckResult(tracked.Count, 0, 0, ["No qBittorrent connection is configured."]);
        }

        var inClient = new Dictionary<string, QBittorrentTorrent>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var client in clients)
        {
            try
            {
                foreach (var torrent in await client.ListTorrentsAsync(cancellationToken))
                {
                    inClient.TryAdd(torrent.Hash, torrent);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{client.Options.BaseUrl}: {Describe(ex)}");
            }
        }

        var found = 0;
        var notInClient = 0;
        foreach (var torrent in tracked)
        {
            if (inClient.TryGetValue(torrent.Id, out var live))
            {
                torrent.ClientStatus = live.State;
                torrent.QBittorrentCategory = live.Category;
                found++;
            }
            else if (errors.Count == 0)
            {
                // Only trustworthy when every instance answered: a torrent missing from the reachable ones may live on the one that did not.
                notInClient++;
                if (torrent.ClientStatus != ClientStatuses.RemovedFromMam)
                {
                    torrent.ClientStatus = ClientStatuses.NotInClient;
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ClientStatusCheckResult(tracked.Count, found, notInClient, errors);
    }

    private static string Describe(Exception exception) => exception switch
    {
        OperationCanceledException or TimeoutException => "did not respond in time",
        HttpRequestException http => http.InnerException?.Message ?? http.Message,
        _ => exception.Message,
    };
}
