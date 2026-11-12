using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <param name="Watched">Torrents still downloading, added to qBittorrent within <see cref="DownloadProgressChecker.WatchFor"/>.</param>
/// <param name="Finished">Of those, how many qBittorrent has finished since the last check: the Link job can take them.</param>
/// <param name="Error">Why a qBittorrent connection couldn't be asked; null when every one could.</param>
public sealed record DownloadProgressResult(int Watched, int Finished, string? Error = null);

/// <summary>
/// One round of following downloads: asks qBittorrent where the torrents still downloading stand, in one request per
/// connection, and stores their state, so the status badges on the Search and Following pages move on whether a page is
/// open or not. Only torrents added within <see cref="WatchFor"/> are asked about: one that hasn't finished by then is
/// left to the Link job's scheduled runs.
/// </summary>
public sealed class DownloadProgressChecker(FetchshelfDbContext dbContext, IReadOnlyList<QBittorrentClient> clients, TimeProvider? timeProvider = null)
{
    /// <summary>How long after it was added to qBittorrent a download is followed.</summary>
    public static readonly TimeSpan WatchFor = TimeSpan.FromHours(24);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<DownloadProgressResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var since = _time.GetUtcNow() - WatchFor;
        var finishedStages = QBittorrentStateLabel.FinishedStages.ToList();
        // Narrowed in the database to unlinked torrents not known to have finished (ClientStatuses.IsUnfinished); the time
        // is compared here, as SQLite can't compare DateTimeOffset values.
        var downloading = (await dbContext.Torrents
                .Where(t => t.LibraryPath == null && t.UnlinkedAt == null
                    && (t.ClientStatus == null || (t.ClientStatus != ClientStatuses.NotInClient && !finishedStages.Contains(t.ClientStatus))))
                .ToListAsync(cancellationToken))
            .Where(t => !MetadataOnlyTorrentId.IsMetadataOnly(t.Id) && t.CreatedAt >= since)
            .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        if (downloading.Count == 0)
        {
            return new DownloadProgressResult(0, 0);
        }

        var finished = 0;
        string? error = null;
        var left = downloading.Keys.ToList();
        foreach (var client in clients)
        {
            if (left.Count == 0)
            {
                break;
            }

            try
            {
                foreach (var live in await client.GetTorrentsAsync(left, cancellationToken))
                {
                    if (downloading.TryGetValue(live.Hash, out var torrent))
                    {
                        ClientStatuses.Update(torrent, live.State);
                        // Finished once qBittorrent says so in its state too: a torrent at 100% that is still being
                        // checked or moved is asked about again, rather than having the Link job started too early.
                        finished += live.IsFinished && QBittorrentStateLabel.IsFinishedStage(live.State) ? 1 : 0;
                        left.Remove(torrent.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                error = ex.Message;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new DownloadProgressResult(downloading.Count - finished, finished, error);
    }
}
