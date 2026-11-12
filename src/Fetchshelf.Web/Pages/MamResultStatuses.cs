using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Web.Pages.Torrents;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// Where MAM torrents stand in Fetchshelf's pipeline, for the status badge the Search and Following pages show
/// (<see cref="SearchResultStatusViewModel"/>), which asks again by itself while a torrent is on its way.
/// </summary>
public static class MamResultStatuses
{
    /// <summary>
    /// Resolves each given MAM id to where it stands in Fetchshelf's pipeline: linked into the
    /// library, downloaded but not yet linked, or still queued as a selection.
    /// </summary>
    /// <param name="dbContext">Fetchshelf's database.</param>
    /// <param name="mamIds">The MAM ids to look up.</param>
    /// <param name="liveStates">
    /// qBittorrent's current state by info hash, when the caller has it: a torrent stored as downloading that qBittorrent
    /// has finished then shows as downloaded, without waiting for the Link job to store the state.
    /// </param>
    public static async Task<Dictionary<long, MamResultStatus>> LoadAsync(
        FetchshelfDbContext dbContext, IReadOnlyCollection<long> mamIds, IReadOnlyDictionary<string, string>? liveStates = null)
    {
        var statuses = new Dictionary<long, MamResultStatus>();
        if (mamIds.Count == 0)
        {
            return statuses;
        }

        var selectedMamIds = await dbContext.SelectedTorrents
            .Where(s => s.RemovedAt == null && s.StartedAt == null && mamIds.Contains(s.MamId))
            .Select(s => s.MamId)
            .ToListAsync();
        foreach (var mamId in selectedMamIds)
        {
            statuses[mamId] = MamResultStatus.Selected;
        }

        var torrents = await dbContext.Torrents
            .Where(t => mamIds.Contains(t.MamId))
            .Select(t => new { t.Id, t.MamId, t.LibraryPath, t.ClientStatus })
            .ToListAsync();
        foreach (var torrent in torrents)
        {
            // The Download job records a torrent as it hands it to qBittorrent, with no client status yet; the Link job
            // stores the status once it sees the torrent finished. Until then it is still downloading. A metadata-only
            // record was never downloaded through Fetchshelf, so it counts as owned.
            var clientStatus = liveStates?.GetValueOrDefault(torrent.Id) ?? torrent.ClientStatus;
            var status = torrent.LibraryPath is not null ? MamResultStatus.Linked
                : !MetadataOnlyTorrentId.IsMetadataOnly(torrent.Id)
                    && (clientStatus is null || QBittorrentStateLabel.IsDownloadStage(clientStatus)) ? MamResultStatus.Downloading
                : MamResultStatus.Downloaded;
            if (status > statuses.GetValueOrDefault(torrent.MamId))
            {
                statuses[torrent.MamId] = status;
            }
        }

        return statuses;
    }
}
