using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <summary>Client status values Fetchshelf sets itself, alongside qBittorrent's own state strings.</summary>
public static class ClientStatuses
{
    public const string NotInClient = "not in client";

    /// <summary>
    /// Stores <paramref name="state"/> as the torrent's client status, unless it is the stage already stored. A seeding
    /// torrent flips between uploading and stalledUP all the time; writing each flip would cost a database write and
    /// change nothing anyone sees.
    /// </summary>
    public static void Update(TorrentEntity torrent, string state)
    {
        if (!QBittorrentStateLabel.SameStage(torrent.ClientStatus, state))
        {
            torrent.ClientStatus = state;
        }
    }
}

/// <param name="Checked">Tracked torrents looked at; metadata-only records are not among them.</param>
/// <param name="FoundInClient">Of those, how many qBittorrent reported (their status and category were refreshed).</param>
/// <param name="NotInClient">Of those, how many no qBittorrent instance has; always 0 when a connection failed.</param>
/// <param name="Errors">One entry per connection that could not be queried, or a single entry if none is configured.</param>
/// <param name="RetiredSelections">Started selections taken off the Selected queue because their torrent has finished.</param>
/// <param name="MetadataOnlySkipped">Metadata-only records left out: without an info hash they cannot be found in a client.</param>
public sealed record ClientStatusCheckResult(
    int Checked, int FoundInClient, int NotInClient, IReadOnlyList<string> Errors, int RetiredSelections = 0, int MetadataOnlySkipped = 0)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Refreshes the stored client status of every tracked torrent from the configured qBittorrent instances, on demand.
/// The Link job only updates finished torrents it processes; this also covers torrents that are still downloading,
/// stopped, or gone from the client. Selections whose torrent turns out to be finished leave the Selected queue.
/// Metadata-only records are skipped, since they have no info hash to look up.
/// </summary>
public sealed class ClientStatusChecker(FetchshelfDbContext dbContext)
{
    public async Task<ClientStatusCheckResult> CheckAsync(IReadOnlyList<QBittorrentClient> clients, CancellationToken cancellationToken = default)
    {
        var all = await dbContext.Torrents.ToListAsync(cancellationToken);
        var metadataOnly = all.Where(t => MetadataOnlyTorrentId.IsMetadataOnly(t.Id)).ToList();
        var tracked = all.Except(metadataOnly).ToList();
        if (clients.Count == 0)
        {
            return new ClientStatusCheckResult(tracked.Count, 0, 0, ["No qBittorrent connection is configured."], MetadataOnlySkipped: metadataOnly.Count);
        }

        // Earlier versions checked these too and marked every one of them not in client.
        foreach (var record in metadataOnly.Where(t => t.ClientStatus == ClientStatuses.NotInClient))
        {
            record.ClientStatus = null;
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
        var finishedMamIds = new List<long>();
        foreach (var torrent in tracked)
        {
            if (inClient.TryGetValue(torrent.Id, out var live))
            {
                ClientStatuses.Update(torrent, live.State);
                torrent.QBittorrentCategory = live.Category;
                found++;
                if (live.IsFinished)
                {
                    finishedMamIds.Add(torrent.MamId);
                }
            }
            else if (errors.Count == 0)
            {
                // Only trustworthy when every instance answered: a torrent missing from the reachable ones may live on the one that did not.
                notInClient++;
                torrent.ClientStatus = ClientStatuses.NotInClient;
            }
        }

        var retired = await SelectionRetirement.RetireStartedAsync(dbContext, finishedMamIds, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ClientStatusCheckResult(tracked.Count, found, notInClient, errors, retired, metadataOnly.Count);
    }

    private static string Describe(Exception exception) => exception switch
    {
        OperationCanceledException or TimeoutException => "did not respond in time",
        HttpRequestException http => http.InnerException?.Message ?? http.Message,
        _ => exception.Message,
    };
}
