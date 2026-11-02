using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What one download run did: torrents started in qBittorrent, and torrents that failed.</summary>
public sealed record DownloadRunSummary(int Started, int Failed);

/// <summary>
/// Runs the downloader: ratio/unsat/wedge safety checks, dedup against every configured client and
/// the library, and handing an approved grab to qBittorrent.
/// </summary>
public sealed class DownloadRunner(
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    MamClient mamClient,
    MamAccessGate accessGate,
    IReadOnlyList<QBittorrentClient> qbClients,
    double minRatio,
    bool addTorrentsStopped,
    ILogger<DownloadRunner>? logger = null)
{
    private readonly ILogger<DownloadRunner> _logger = logger ?? NullLogger<DownloadRunner>.Instance;

    /// <summary>
    /// Starts queued selections within the account's limits. A failure on one torrent is logged and the run
    /// continues; MAM refusing the account (rate limit, block, 403) ends the run, since every later request would fail too.
    /// </summary>
    public async Task<DownloadRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync(cancellationToken);

        // SQLite's EF provider can't translate ORDER BY on a DateTimeOffset column, so order client-side.
        var pending = (await dbContext.SelectedTorrents
            .Where(s => s.StartedAt == null && s.RemovedAt == null)
            .ToListAsync(cancellationToken))
            .OrderBy(s => s.CreatedAt)
            .ToList();

        if (pending.Count == 0)
        {
            _logger.LogInformation("No selected torrents are waiting to be downloaded");
            return new DownloadRunSummary(0, 0);
        }

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var bytesInFlight = await SumInFlightBytesAsync(cancellationToken);
        var budget = new DownloadRunBudget(
            account.UnsatLimit, account.UnsatCount, account.Wedges,
            DownloadBudget.ComputeRatioBudgetBytes((long)account.UploadedBytes, (long)account.DownloadedBytes, bytesInFlight, minRatio));

        var (startedCount, failedCount) = (0, 0);
        foreach (var selected in pending)
        {
            var name = TorrentMetadataSerializer.Deserialize(selected.MetadataJson).DisplayName();
            DownloadOutcome outcome;
            bool wedgeSpent;
            try
            {
                (outcome, wedgeSpent) = await ProcessOneAsync(selected, budget, cancellationToken);
            }
            catch (Exception ex) when (ex is MamAccountBlockedException or MamRateLimitedException or MamForbiddenException)
            {
                _logger.LogError("Stopped downloading at {Torrent}: {Error} The rest stay queued for the next run", name, ex.Message);
                failedCount++;
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Could not download {Torrent}: {Error} It stays queued for the next run", name, ex.Message);
                failedCount++;
                if (Enum.TryParse<SelectedTorrentCost>(selected.CostDecision, out var failedCost))
                {
                    budget.Failed(failedCost);
                }

                dbContext.DiscardPendingChanges();
                continue;
            }

            LogOutcome(selected, name, outcome, wedgeSpent);
            if (outcome == DownloadOutcome.LinkStillStaleAfterRefresh)
            {
                failedCount++;
            }

            if (wedgeSpent)
            {
                budget.WedgeSpent();
            }

            if (outcome == DownloadOutcome.Started)
            {
                startedCount++;
                budget.Started(
                    Enum.Parse<SelectedTorrentCost>(selected.CostDecision),
                    TorrentMetadataSerializer.Deserialize(selected.MetadataJson).TotalSizeBytes);
            }
        }

        return new DownloadRunSummary(startedCount, failedCount);
    }

    private void LogOutcome(SelectedTorrentEntity selected, string name, DownloadOutcome outcome, bool wedgeSpent)
    {
        switch (outcome)
        {
            case DownloadOutcome.Started:
                _logger.LogInformation(
                    "Added {Torrent} to qBittorrent (cost {Cost}{Wedge}, category {Category})",
                    name, selected.CostDecision, wedgeSpent ? ", wedge used" : string.Empty,
                    string.IsNullOrEmpty(selected.Category) ? "none" : selected.Category);
                break;
            case DownloadOutcome.NoUnsatSlot:
                _logger.LogInformation("{Torrent} stays queued: no unsatisfied-snatch slot is free beyond unsat_buffer", name);
                break;
            case DownloadOutcome.OverRatioBudget:
                _logger.LogInformation("{Torrent} stays queued: downloading it would take the ratio below min_ratio", name);
                break;
            case DownloadOutcome.WedgeUnavailable:
                _logger.LogInformation("{Torrent} stays queued: no freeleech wedge to spare beyond wedge_buffer", name);
                break;
            case DownloadOutcome.NoLongerFree:
                _logger.LogInformation("{Torrent} stays queued: it is no longer free on MAM", name);
                break;
            case DownloadOutcome.LinkStillStaleAfterRefresh:
                _logger.LogError("Could not download {Torrent}: MAM gave no working download link. Removed from the queue; see the Errors page", name);
                break;
            case DownloadOutcome.AlreadyFinishedInClient:
                _logger.LogInformation("{Torrent} is already finished in qBittorrent; removed from the queue", name);
                break;
            case DownloadOutcome.AlreadyDownloadingInClient:
                _logger.LogInformation("{Torrent} is already downloading in qBittorrent; marked as started", name);
                break;
            case DownloadOutcome.AlreadyLinked:
                _logger.LogInformation("{Torrent} is already in your library; removed from the queue", name);
                break;
            default:
                _logger.LogInformation("{Torrent}: {Outcome}", name, outcome);
                break;
        }
    }

    private async Task<long> SumInFlightBytesAsync(CancellationToken cancellationToken)
    {
        var ratioSpendingCosts = Enum.GetValues<SelectedTorrentCost>().Where(DownloadBudget.SpendsRatio).Select(c => c.ToString()).ToList();
        var startedNonFree = await dbContext.SelectedTorrents
            .Where(s => s.StartedAt != null && s.RemovedAt == null)
            .Where(s => ratioSpendingCosts.Contains(s.CostDecision))
            .Select(s => s.MetadataJson)
            .ToListAsync(cancellationToken);

        return startedNonFree.Sum(json => TorrentMetadataSerializer.Deserialize(json).TotalSizeBytes);
    }

    /// <summary>The outcome, and whether the torrent file was fetched with a wedge (which spends it on MAM).</summary>
    private async Task<(DownloadOutcome Outcome, bool WedgeSpent)> ProcessOneAsync(
        SelectedTorrentEntity selected, DownloadRunBudget budget, CancellationToken cancellationToken)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(selected.MetadataJson);
        var cost = Enum.Parse<SelectedTorrentCost>(selected.CostDecision);

        if (!DownloadBudget.HasFreeUnsatSlot(budget.UnsatLimit, budget.UnsatCount, selected.UnsatBuffer))
        {
            return (DownloadOutcome.NoUnsatSlot, false);
        }

        return await ProcessWithinBudgetsAsync(selected, metadata, cost, budget.WedgesAvailable, budget.RatioBudgetBytes, cancellationToken);
    }

    private async Task<(DownloadOutcome Outcome, bool WedgeSpent)> ProcessWithinBudgetsAsync(
        SelectedTorrentEntity selected,
        Fetchshelf.Core.Metadata.TorrentMetadata metadata,
        SelectedTorrentCost cost,
        long wedgesAvailable,
        long ratioBudgetBytes,
        CancellationToken cancellationToken)
    {
        if (!DownloadBudget.FitsRatioBudget(cost, metadata.TotalSizeBytes, ratioBudgetBytes))
        {
            return (DownloadOutcome.OverRatioBudget, false);
        }

        var useWedge = false;
        if (cost is SelectedTorrentCost.UseWedge or SelectedTorrentCost.TryWedge)
        {
            var isAlreadyFree = false;
            if (cost == SelectedTorrentCost.TryWedge)
            {
                var current = await FetchCurrentRecordAsync(selected.MamId, cancellationToken);
                isAlreadyFree = IsFreeNow(current);
            }

            var decision = DownloadBudget.DecideWedge(cost, wedgesAvailable, selected.WedgeBuffer, isAlreadyFree);
            if (decision == WedgeDecision.Fail)
            {
                return (DownloadOutcome.WedgeUnavailable, false);
            }

            useWedge = decision == WedgeDecision.UseWedge;
        }

        var torrentBytes = await FetchTorrentFileAsync(selected, useWedge, cancellationToken);
        if (torrentBytes is null)
        {
            return (DownloadOutcome.LinkStillStaleAfterRefresh, false);
        }

        var hash = TorrentFileInfoHash.Compute(torrentBytes);

        var dedupOutcome = await CheckExistingAsync(selected, metadata, hash, cancellationToken);
        if (dedupOutcome is not null)
        {
            return (dedupOutcome.Value, useWedge);
        }

        if (cost is SelectedTorrentCost.Vip or SelectedTorrentCost.PersonalFreeleech or SelectedTorrentCost.GlobalFreeleech)
        {
            var current = await FetchCurrentRecordAsync(selected.MamId, cancellationToken);
            var stillFree = IsFreeNow(current);
            if (!stillFree)
            {
                return (DownloadOutcome.NoLongerFree, useWedge);
            }
        }

        await AddToQBittorrentAsync(selected, metadata, hash, torrentBytes, cost, useWedge, cancellationToken);
        return (DownloadOutcome.Started, useWedge);
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

    private static bool IsFreeNow(MamTorrentRecord? current) =>
        current is { } c && (c.IsVip || c.IsPersonalFreeleech || c.IsFree);
}
