using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Runs the downloader: ratio/unsat/wedge safety checks, dedup against every configured client and
/// the library, and handing an approved grab to qBittorrent.
/// </summary>
public sealed class DownloadRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    MamClient mamClient,
    IReadOnlyList<QBittorrentClient> qbClients,
    double minRatio,
    bool addTorrentsStopped)
{
    /// <summary>Returns the number of torrents successfully started this run.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var unsatLimit = account.UnsatLimit;
        var unsatCount = account.UnsatCount;
        var wedgesAvailable = account.Wedges;
        var bytesInFlight = await SumInFlightBytesAsync(cancellationToken);
        var ratioBudgetBytes = DownloadBudget.ComputeRatioBudgetBytes(
            (long)account.UploadedBytes, (long)account.DownloadedBytes, bytesInFlight, minRatio);

        // SQLite's EF provider can't translate ORDER BY on a DateTimeOffset column, so order client-side.
        var pending = (await dbContext.SelectedTorrents
            .Where(s => s.StartedAt == null && s.RemovedAt == null)
            .ToListAsync(cancellationToken))
            .OrderBy(s => s.CreatedAt)
            .ToList();

        var startedCount = 0;
        foreach (var selected in pending)
        {
            DownloadOutcome outcome;
            try
            {
                outcome = await ProcessOneAsync(selected, unsatLimit, unsatCount, wedgesAvailable, ratioBudgetBytes, cancellationToken);
            }
            catch (MamAccountBlockedException)
            {
                break;
            }

            if (outcome == DownloadOutcome.Started)
            {
                startedCount++;
                unsatCount++;
                var metadata = TorrentMetadataSerializer.Deserialize(selected.MetadataJson);
                if (selected.CostDecision == nameof(SelectedTorrentCost.UseWedge))
                {
                    wedgesAvailable--;
                }

                if (selected.CostDecision is nameof(SelectedTorrentCost.Ratio) or nameof(SelectedTorrentCost.UseWedge) or nameof(SelectedTorrentCost.TryWedge))
                {
                    ratioBudgetBytes -= metadata.TotalSizeBytes;
                }
            }
        }

        return startedCount;
    }

    private async Task<long> SumInFlightBytesAsync(CancellationToken cancellationToken)
    {
        var startedNonFree = await dbContext.SelectedTorrents
            .Where(s => s.StartedAt != null && s.RemovedAt == null)
            .Where(s => s.CostDecision == nameof(SelectedTorrentCost.Ratio)
                || s.CostDecision == nameof(SelectedTorrentCost.UseWedge)
                || s.CostDecision == nameof(SelectedTorrentCost.TryWedge))
            .Select(s => s.MetadataJson)
            .ToListAsync(cancellationToken);

        return startedNonFree.Sum(json => TorrentMetadataSerializer.Deserialize(json).TotalSizeBytes);
    }

    private async Task<DownloadOutcome> ProcessOneAsync(
        SelectedTorrentEntity selected, long unsatLimit, long unsatCount, long wedgesAvailable, long ratioBudgetBytes, CancellationToken cancellationToken)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(selected.MetadataJson);
        var cost = Enum.Parse<SelectedTorrentCost>(selected.CostDecision);

        if (!DownloadBudget.HasFreeUnsatSlot(unsatLimit, unsatCount, selected.UnsatBuffer))
        {
            return DownloadOutcome.NoUnsatSlot;
        }

        return await ProcessWithinBudgetsAsync(selected, metadata, cost, wedgesAvailable, ratioBudgetBytes, cancellationToken);
    }

    private async Task<DownloadOutcome> ProcessWithinBudgetsAsync(
        SelectedTorrentEntity selected,
        Fetchshelf.Core.Metadata.TorrentMetadata metadata,
        SelectedTorrentCost cost,
        long wedgesAvailable,
        long ratioBudgetBytes,
        CancellationToken cancellationToken)
    {
        if (!DownloadBudget.FitsRatioBudget(cost, metadata.TotalSizeBytes, ratioBudgetBytes))
        {
            return DownloadOutcome.OverRatioBudget;
        }

        var useWedge = false;
        if (cost is SelectedTorrentCost.UseWedge or SelectedTorrentCost.TryWedge)
        {
            var isAlreadyFree = false;
            if (cost == SelectedTorrentCost.TryWedge)
            {
                var current = await FetchCurrentRecordAsync(selected.MamId, cancellationToken);
                isAlreadyFree = current is { } c && (c.IsVip || c.IsPersonalFreeleech || c.IsFree);
            }

            var decision = DownloadBudget.DecideWedge(cost, wedgesAvailable, selected.WedgeBuffer, isAlreadyFree);
            if (decision == WedgeDecision.Fail)
            {
                return DownloadOutcome.WedgeUnavailable;
            }

            useWedge = decision == WedgeDecision.UseWedge;
        }

        var torrentBytes = await FetchTorrentFileAsync(selected, useWedge, cancellationToken);
        if (torrentBytes is null)
        {
            return DownloadOutcome.LinkStillStaleAfterRefresh;
        }

        var hash = TorrentFileInfoHash.Compute(torrentBytes);

        var dedupOutcome = await CheckExistingAsync(selected, metadata, hash, cancellationToken);
        if (dedupOutcome is not null)
        {
            return dedupOutcome.Value;
        }

        if (cost is SelectedTorrentCost.Vip or SelectedTorrentCost.PersonalFreeleech or SelectedTorrentCost.GlobalFreeleech)
        {
            var current = await FetchCurrentRecordAsync(selected.MamId, cancellationToken);
            var stillFree = current is { } c && (c.IsVip || c.IsPersonalFreeleech || c.IsFree);
            if (!stillFree)
            {
                return DownloadOutcome.NoLongerFree;
            }
        }

        await AddToQBittorrentAsync(selected, metadata, hash, torrentBytes, cost, useWedge, cancellationToken);
        return DownloadOutcome.Started;
    }

    private async Task<MamTorrentRecord?> FetchCurrentRecordAsync(long mamId, CancellationToken cancellationToken)
    {
        var result = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId }, cancellationToken);
        return result.Data.FirstOrDefault();
    }

    private async Task<byte[]?> FetchTorrentFileAsync(SelectedTorrentEntity selected, bool useWedge, CancellationToken cancellationToken)
    {
        try
        {
            return await mamClient.DownloadTorrentFileAsync(selected.DownloadLink, selected.MamId, useWedge, cancellationToken);
        }
        catch (MamAccountBlockedException)
        {
            throw;
        }
        catch
        {
            var current = await FetchCurrentRecordAsync(selected.MamId, cancellationToken);
            if (current?.DownloadHash is not { } refreshedLink)
            {
                await RemoveStaleSelectionAsync(selected, cancellationToken);
                return null;
            }

            selected.DownloadLink = refreshedLink;

            try
            {
                return await mamClient.DownloadTorrentFileAsync(refreshedLink, selected.MamId, useWedge, cancellationToken);
            }
            catch (MamAccountBlockedException)
            {
                throw;
            }
            catch
            {
                await RemoveStaleSelectionAsync(selected, cancellationToken);
                return null;
            }
        }
    }

    private async Task RemoveStaleSelectionAsync(SelectedTorrentEntity selected, CancellationToken cancellationToken)
    {
        selected.RemovedAt = DateTimeOffset.UtcNow;
        var restorePayload = new GrabberErrorRestorePayload
        {
            MamId = selected.MamId,
            DownloadLink = selected.DownloadLink,
            CostDecision = selected.CostDecision,
            Category = selected.Category,
            TagsJson = selected.TagsJson,
            UnsatBuffer = selected.UnsatBuffer,
            WedgeBuffer = selected.WedgeBuffer,
            GrabberName = selected.GrabberName,
            MetadataJson = selected.MetadataJson,
            NormalizedTitleKey = selected.NormalizedTitleKey,
        };
        await ErrorLog.RecordAsync(
            dbContext,
            ErrorStage.Grabber,
            selected.MamId.ToString(),
            TorrentMetadataSerializer.Deserialize(selected.MetadataJson).Title,
            "Download link is stale and could not be refreshed.",
            System.Text.Json.JsonSerializer.Serialize(restorePayload),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<DownloadOutcome?> CheckExistingAsync(
        SelectedTorrentEntity selected, Fetchshelf.Core.Metadata.TorrentMetadata metadata, string hash, CancellationToken cancellationToken)
    {
        foreach (var client in qbClients)
        {
            var torrents = await client.ListTorrentsAsync(cancellationToken);
            var match = torrents.FirstOrDefault(t => string.Equals(t.Hash, hash, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                continue;
            }

            if (match.IsFinished)
            {
                CreateTorrentRecord(hash, metadata);
                selected.RemovedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return DownloadOutcome.AlreadyFinishedInClient;
            }

            selected.StartedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return DownloadOutcome.AlreadyDownloadingInClient;
        }

        if (await dbContext.Torrents.AnyAsync(t => t.Id == hash, cancellationToken))
        {
            selected.RemovedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return DownloadOutcome.AlreadyLinked;
        }

        return null;
    }

    private Task ClearGrabberErrorAsync(long mamId, CancellationToken cancellationToken) =>
        ErrorLog.ClearAsync(dbContext, ErrorStage.Grabber, mamId.ToString(), cancellationToken);

    private void CreateTorrentRecord(string hash, Fetchshelf.Core.Metadata.TorrentMetadata metadata) =>
        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = hash,
            MamId = metadata.MamId,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private async Task AddToQBittorrentAsync(
        SelectedTorrentEntity selected,
        Fetchshelf.Core.Metadata.TorrentMetadata metadata,
        string hash,
        byte[] torrentBytes,
        SelectedTorrentCost cost,
        bool wedgeUsed,
        CancellationToken cancellationToken)
    {
        var tags = selected.TagsJson is null
            ? []
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(selected.TagsJson) ?? [];

        var client = qbClients[0];
        await client.AddTorrentAsync(torrentBytes, selected.Category ?? string.Empty, tags, addTorrentsStopped, cancellationToken);

        CreateTorrentRecord(hash, metadata);
        selected.StartedAt = DateTimeOffset.UtcNow;
        await ClearGrabberErrorAsync(selected.MamId, cancellationToken);

        await eventStore.AppendAsync(new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = hash,
            MamId = selected.MamId,
            Kind = EventKind.Grabbed,
            GrabberName = selected.GrabberName,
            Cost = cost.ToString(),
            WedgeUsed = wedgeUsed,
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
