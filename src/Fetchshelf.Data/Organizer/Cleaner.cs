using System.Text.Json;
using Fetchshelf.Clients.Abs;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Organizer;

/// <summary>What one clean run did: superseded copies retired, and copies whose retirement failed.</summary>
public sealed record CleanRunSummary(int Retired, int Failed);

/// <summary>Whether an unlink went ahead, and what it did or why it did not.</summary>
public sealed record UnlinkResult(bool Succeeded, string Message);

/// <summary>
/// Takes copies out of the library: superseded duplicates among linked torrents, and torrents unlinked by hand.
/// </summary>
public sealed class Cleaner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    FormatPreferenceLists formatPreferenceLists,
    AbsClient? absClient = null,
    ILogger<Cleaner>? logger = null)
{
    private readonly ILogger<Cleaner> _logger = logger ?? NullLogger<Cleaner>.Instance;

    /// <summary>Retires every superseded copy; a failure on one is logged and recorded as a clean error, and the run continues.</summary>
    /// <param name="connections">Every configured qBittorrent connection, each paired with its client.</param>
    public async Task<CleanRunSummary> RunAsync(
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default)
    {
        await UnmarkReplacedCopiesLinkedAgainAsync(cancellationToken);

        var linked = await dbContext.Torrents
            .Where(t => t.LibraryPath != null && t.ReplacedWithTorrentId == null)
            .ToListAsync(cancellationToken);

        var duplicateGroups = DuplicateCopies.GroupBySameBook(linked, t => TorrentMetadataSerializer.Deserialize(t.MetadataJson))
            .Where(g => g.Count >= 2)
            .ToList();
        if (duplicateGroups.Count == 0)
        {
            _logger.LogInformation("No duplicate copies to clean up");
            return new CleanRunSummary(0, 0);
        }

        var (retired, failed) = (0, 0);
        foreach (var group in duplicateGroups)
        {
            var keeper = PickKeeper(group);
            foreach (var member in group.Where(m => m.Id != keeper.Id))
            {
                // The automatic pass only deletes the member's ABS item when the keeper ends
                // up at a different library path.
                var forceAbsDeletion = keeper.LibraryPath is not null && keeper.LibraryPath != member.LibraryPath;
                try
                {
                    await RetireAsync(member, keeper, connections, forceAbsDeletion, cancellationToken);
                    retired++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // One copy that cannot be retired must not stop the rest; it is tried again next run.
                    failed++;
                    await RecordCleanErrorAsync(member, keeper, ex.Message, cancellationToken);
                }
            }
        }

        return new CleanRunSummary(retired, failed);
    }

    /// <summary>
    /// Earlier versions of the Link job linked retired copies straight back, and this job then skipped them because
    /// they were still marked replaced. Unmarking them lets this run weigh them against the other copies again.
    /// </summary>
    private async Task UnmarkReplacedCopiesLinkedAgainAsync(CancellationToken cancellationToken)
    {
        var linkedAgain = await dbContext.Torrents
            .Where(t => t.LibraryPath != null && t.ReplacedWithTorrentId != null)
            .ToListAsync(cancellationToken);
        if (linkedAgain.Count == 0)
        {
            return;
        }

        foreach (var torrent in linkedAgain)
        {
            torrent.ReplacedWithTorrentId = null;
            torrent.ReplacedAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "{Count} replaced {Copies} had been linked again; checking them against the other copies again",
            linkedAgain.Count, linkedAgain.Count == 1 ? "copy" : "copies");
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
            if (!DuplicateCopies.AreCopiesOfSameBook(targetMetadata, otherMetadata))
            {
                continue;
            }

            var keeper = PickKeeper([target, other]);
            var loser = keeper.Id == target.Id ? other : target;
            // Manual clean always deletes the loser's ABS item when one is stored, regardless of path.
            await RetireAsync(loser, keeper, connections, deleteAbsItem: true, cancellationToken);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Takes a torrent's files out of the library and applies `on_unlinked` to it in qBittorrent, which keeps the
    /// torrent and its data. The record stays, marked unlinked, so the book still counts as owned and the Link job
    /// leaves it alone until it is relinked.
    /// </summary>
    public async Task<UnlinkResult> UnlinkAsync(
        string torrentId, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        CancellationToken cancellationToken = default)
    {
        var torrent = await dbContext.Torrents.FirstOrDefaultAsync(t => t.Id == torrentId, cancellationToken)
            ?? throw new InvalidOperationException($"Torrent '{torrentId}' is not tracked.");
        if (MetadataOnlyTorrentId.IsMetadataOnly(torrent.Id))
        {
            return new UnlinkResult(false, "Not unlinked: Fetchshelf never downloaded this torrent, so it has nothing to unlink.");
        }

        if (torrent.UnlinkedAt is not null)
        {
            return new UnlinkResult(false, "Not unlinked: it already is.");
        }

        var details = await RetireAsync(torrent, keeper: null, connections, deleteAbsItem: true, cancellationToken);
        return new UnlinkResult(true, $"Unlinked: {string.Join("; ", details)}.");
    }

    private TorrentEntity PickKeeper(IReadOnlyList<TorrentEntity> group) =>
        DuplicateCopies.PickKeeper(group, Rank, TotalLinkedSize);

    private int Rank(TorrentEntity torrent)
    {
        if (torrent.ChosenAudioExtension is not null)
        {
            var mediaType = TorrentMetadataSerializer.Deserialize(torrent.MetadataJson).MediaType;
            return FormatRanker.Rank(formatPreferenceLists.AudioSideFor(mediaType), [torrent.ChosenAudioExtension]);
        }

        if (torrent.ChosenEbookExtension is not null)
        {
            return FormatRanker.Rank(formatPreferenceLists.EbookTypes, [torrent.ChosenEbookExtension]);
        }

        return FormatRanker.NotPresent;
    }

    private static long TotalLinkedSize(TorrentEntity torrent) =>
        DeserializeLinkedFiles(torrent.LinkedFilesJson).Sum(f => f.SizeBytes);

    private static List<LinkedFileRecord> DeserializeLinkedFiles(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(json) ?? [];

    /// <summary>
    /// Removes what Fetchshelf placed in the library for <paramref name="member"/>, applies `on_cleaned` (with a
    /// <paramref name="keeper"/>) or `on_unlinked` (without one) in qBittorrent, and marks it replaced or unlinked.
    /// Returns what was done, for the log and the page.
    /// </summary>
    private async Task<List<string>> RetireAsync(
        TorrentEntity member, TorrentEntity? keeper,
        IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections,
        bool deleteAbsItem,
        CancellationToken cancellationToken)
    {
        var settingName = keeper is null ? "on_unlinked" : "on_cleaned";
        QBittorrentCategoryAndTags? SettingOf(QBittorrentConnection? connection) =>
            keeper is null ? connection?.OnUnlinked : connection?.OnCleaned;

        var details = new List<string>();
        var owning = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), member.Id, cancellationToken);
        if (owning is null)
        {
            if (connections.Any(c => SettingOf(c.Connection) is not null))
            {
                details.Add($"not found in qBittorrent, so {settingName} was not applied");
            }
        }
        else if (SettingOf(connections.First(c => c.Client == owning).Connection) is { } setting)
        {
            if (!string.IsNullOrEmpty(setting.Category))
            {
                await owning.SetCategoryAsync(member.Id, setting.Category, cancellationToken);
            }

            if (setting.Tags.Count > 0)
            {
                await owning.AddTagsAsync(member.Id, setting.Tags, cancellationToken);
            }

            details.Add($"applied {settingName} in qBittorrent");
        }

        var removedFiles = new List<string>();
        if (member.LibraryPath is not null)
        {
            var shared = await LibraryFileRemover.IsFolderSharedAsync(dbContext, member.LibraryPath, member.Id, cancellationToken);
            removedFiles = LibraryFileRemover.Remove(member.LibraryPath, DeserializeLinkedFiles(member.LinkedFilesJson), shared);
            details.Insert(0, $"removed {removedFiles.Count} {(removedFiles.Count == 1 ? "file" : "files")} from {member.LibraryPath}");
        }
        else
        {
            details.Insert(0, "it was not linked, so no files were removed");
        }

        if (deleteAbsItem && member.AbsId is not null && absClient is not null)
        {
            await absClient.DeleteItemAsync(member.AbsId, cancellationToken);
            details.Add($"deleted Audiobookshelf item {member.AbsId}");
        }

        var oldLibraryPath = member.LibraryPath;
        if (keeper is null)
        {
            member.UnlinkedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            member.ReplacedWithTorrentId = keeper.Id;
            member.ReplacedAt = DateTimeOffset.UtcNow;
        }

        member.LibraryPath = null;
        member.LinkedFilesJson = null;
        member.AbsId = null;
        member.LibraryMismatchState = null;

        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = member.Id,
            MamId = member.MamId,
            Kind = keeper is null ? EventKind.Unlinked : EventKind.Cleaned,
            LinkerName = member.LinkerName,
            LibraryPath = oldLibraryPath,
            FilesRemovedJson = JsonSerializer.Serialize(removedFiles),
        }, cancellationToken);

        await ErrorLog.ClearAsync(dbContext, ErrorStage.Cleaner, member.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (keeper is null)
        {
            _logger.LogInformation(
                "Unlinked {Title} (MAM id {MamId}, {Format}): {Details}",
                member.Title, member.MamId, FormatOf(member), string.Join("; ", details));
        }
        else
        {
            _logger.LogInformation(
                "Retired {Title} (MAM id {MamId}, {Format}) in favour of MAM id {KeeperMamId} ({KeeperFormat}): {Details}",
                member.Title, member.MamId, FormatOf(member), keeper.MamId, FormatOf(keeper), string.Join("; ", details));
        }

        return details;
    }

    private async Task RecordCleanErrorAsync(TorrentEntity member, TorrentEntity keeper, string errorText, CancellationToken cancellationToken)
    {
        _logger.LogError(
            "Could not retire {Title} (MAM id {MamId}) in favour of MAM id {KeeperMamId}: {Error}",
            member.Title, member.MamId, keeper.MamId, errorText);

        dbContext.DiscardPendingChanges();
        await ErrorLog.RecordAsync(dbContext, ErrorStage.Cleaner, member.Id, member.Title, errorText, cancellationToken: cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string FormatOf(TorrentEntity torrent) =>
        torrent.ChosenAudioExtension ?? torrent.ChosenEbookExtension ?? "no format";
}
