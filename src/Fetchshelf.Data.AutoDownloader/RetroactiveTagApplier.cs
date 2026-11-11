using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Applies one `[[tag]]` rule to already-stored library torrents on demand.
/// Distinct from grab-time tag resolution (<see cref="TagRuleResolver"/>), which runs inside
/// <see cref="SelectionRunner"/> instead.
/// </summary>
public sealed class RetroactiveTagApplier(
    FetchshelfDbContext dbContext,
    MamClient mamClient,
    MamAccessGate accessGate,
    IReadOnlyList<QBittorrentClient> qbittorrentClients)
{
    /// <summary>Returns the number of torrents whose category or tags were set in qBittorrent.</summary>
    public async Task<int> ApplyAsync(TagRule rule, CancellationToken cancellationToken = default)
    {
        var torrents = await dbContext.Torrents.ToListAsync(cancellationToken);
        var applied = 0;

        foreach (var torrent in torrents)
        {
            if (MetadataOnlyTorrentId.IsMetadataOnly(torrent.Id))
            {
                continue;
            }

            var metadata = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson);
            var candidate = await BuildCandidateAsync(rule.Filter, metadata, cancellationToken);
            if (!TorrentFilterMatcher.Matches(rule.Filter, candidate))
            {
                continue;
            }

            var owning = await QBittorrentConnectionResolver.ResolveAsync(qbittorrentClients, torrent.Id, cancellationToken);
            if (owning is null)
            {
                continue;
            }

            if (rule.Category is not null)
            {
                await owning.SetCategoryAsync(torrent.Id, rule.Category, cancellationToken);
            }

            await owning.AddTagsAsync(torrent.Id, rule.Tags, cancellationToken);
            applied++;
        }

        return applied;
    }

    private async Task<TorrentFilterCandidate> BuildCandidateAsync(
        TorrentFilter filter, TorrentMetadata metadata, CancellationToken cancellationToken)
    {
        if (!filter.NeedsLiveData)
        {
            return new TorrentFilterCandidate
            {
                Metadata = metadata,
                UploaderName = string.Empty,
                Seeders = 0,
                Leechers = 0,
                TimesCompleted = 0,
            };
        }

        MamSearchResult result;
        using (await accessGate.AcquireAsync("Tagging a torrent", cancellationToken))
        {
            result = await mamClient.SearchAsync(new MamSearchQuery { Id = metadata.MamId }, cancellationToken);
        }

        var record = result.Data.FirstOrDefault();

        return new TorrentFilterCandidate
        {
            Metadata = metadata,
            UploaderName = record?.UploaderName ?? string.Empty,
            Seeders = record?.Seeders ?? 0,
            Leechers = record?.Leechers ?? 0,
            TimesCompleted = record?.TimesCompleted ?? 0,
        };
    }
}
