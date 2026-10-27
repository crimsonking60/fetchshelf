using System.Text.Json;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <summary>Retires superseded duplicates among linked torrents</summary>
public sealed class Cleaner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    IReadOnlyList<string> globalAudioTypes,
    IReadOnlyList<string> globalEbookTypes,
    AbsClient? absClient = null)
{
    /// <param name="connections">Every configured qBittorrent connection, each paired with its client.</param>
    public async Task RunAsync(
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default)
    {
        var linked = await dbContext.Torrents
            .Where(t => t.LibraryPath != null && t.ReplacedWithTorrentId == null)
            .ToListAsync(cancellationToken);

        foreach (var group in GroupBySameBook(linked))
        {
            if (group.Count < 2)
            {
                continue;
            }

            var keeper = PickKeeper(group);
            foreach (var member in group.Where(m => m.Id != keeper.Id))
            {
                // The automatic pass only deletes the member's ABS item when the keeper ends
                // up at a different library path.
                var forceAbsDeletion = keeper.LibraryPath is not null && keeper.LibraryPath != member.LibraryPath;
                await ReplaceAsync(member, keeper, connections, forceAbsDeletion, cancellationToken);
            }
        }
    }

    /// <summary>Cleans a single torrent against the rest of the currently-linked set (manual "clean" action).</summary>
    public async Task<bool> CleanOneAsync(
        string torrentId, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default)
    {
        var target = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken);
        if (target?.LibraryPath is null)
        {
            return false;
        }

        var targetMetadata = TorrentMetadataSerializer.Deserialize(target.MetadataJson);
        var others = await dbContext.Torrents
            .Where(t => t.LibraryPath != null && t.ReplacedWithTorrentId == null && t.Id != torrentId)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            var otherMetadata = TorrentMetadataSerializer.Deserialize(other.MetadataJson);
            if (!SameBookComparer.AreSameBook(targetMetadata, otherMetadata))
            {
                continue;
            }

            var keeper = PickKeeper([target, other]);
            var loser = keeper.Id == target.Id ? other : target;
            // Manual clean always deletes the loser's ABS item when one is stored, regardless of path.
            await ReplaceAsync(loser, keeper, connections, forceAbsDeletion: true, cancellationToken);
            return true;
        }

        return false;
    }

    private List<List<TorrentEntity>> GroupBySameBook(IReadOnlyList<TorrentEntity> linked)
    {
        var metadataById = linked.ToDictionary(t => t.Id, t => TorrentMetadataSerializer.Deserialize(t.MetadataJson));
        var groups = new List<List<TorrentEntity>>();

        foreach (var torrent in linked)
        {
            var group = groups.FirstOrDefault(g => SameBookComparer.AreSameBook(metadataById[g[0].Id], metadataById[torrent.Id]));
            if (group is not null)
            {
                group.Add(torrent);
            }
            else
            {
                groups.Add([torrent]);
            }
        }

        return groups;
    }

    private TorrentEntity PickKeeper(IReadOnlyList<TorrentEntity> group) => group
        .OrderBy(Rank)
        .ThenByDescending(TotalLinkedSize)
        .First();

    private int Rank(TorrentEntity torrent)
    {
        if (torrent.ChosenAudioExtension is not null)
        {
            return FormatRanker.Rank(globalAudioTypes, [torrent.ChosenAudioExtension]);
        }

        if (torrent.ChosenEbookExtension is not null)
        {
            return FormatRanker.Rank(globalEbookTypes, [torrent.ChosenEbookExtension]);
        }

        return FormatRanker.NotPresent;
    }

    private static long TotalLinkedSize(TorrentEntity torrent) =>
        DeserializeLinkedFiles(torrent.LinkedFilesJson).Sum(f => f.SizeBytes);

    private static List<LinkedFileRecord> DeserializeLinkedFiles(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(json) ?? [];

    private async Task ReplaceAsync(
        TorrentEntity member, TorrentEntity keeper,
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        bool forceAbsDeletion,
        CancellationToken cancellationToken)
    {
        var owning = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), member.Id, cancellationToken);
        var onCleaned = connections.FirstOrDefault(c => c.Client == owning).Connection?.OnCleaned;
        if (owning is not null && onCleaned is not null)
        {
            if (!string.IsNullOrEmpty(onCleaned.Category))
            {
                await owning.SetCategoryAsync(member.Id, onCleaned.Category, cancellationToken);
            }

            if (onCleaned.Tags.Count > 0)
            {
                await owning.AddTagsAsync(member.Id, onCleaned.Tags, cancellationToken);
            }
        }

        var removedFiles = new List<string>();
        if (member.LibraryPath is not null)
        {
            foreach (var file in DeserializeLinkedFiles(member.LinkedFilesJson))
            {
                var path = Path.Combine(member.LibraryPath, file.RelativePath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    removedFiles.Add(file.RelativePath);
                }
            }

            foreach (var extra in new[] { "cover.jpg", "metadata.json" })
            {
                var path = Path.Combine(member.LibraryPath, extra);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (Directory.Exists(member.LibraryPath) && Directory.GetFileSystemEntries(member.LibraryPath).Length == 0)
            {
                Directory.Delete(member.LibraryPath);
            }
        }

        if (forceAbsDeletion && member.AbsId is not null && absClient is not null)
        {
            await absClient.DeleteItemAsync(member.AbsId, cancellationToken);
        }

        var oldLibraryPath = member.LibraryPath;
        member.ReplacedWithTorrentId = keeper.Id;
        member.ReplacedAt = DateTimeOffset.UtcNow;
        member.LibraryPath = null;
        member.LinkedFilesJson = null;
        member.AbsId = null;
        member.LibraryMismatchState = null;

        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = member.Id,
            MamId = member.MamId,
            Kind = EventKind.Cleaned,
            LinkerName = member.LinkerName,
            LibraryPath = oldLibraryPath,
            FilesRemovedJson = JsonSerializer.Serialize(removedFiles),
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
