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

/// <summary>What one download run did: torrents started in qBittorrent, torrents that failed, and how many were queued.</summary>
/// <param name="Started">Torrents added to qBittorrent.</param>
/// <param name="Failed">Torrents that couldn't be downloaded.</param>
/// <param name="Queued">Selections waiting when the run began.</param>
/// <param name="LeftQueued">Selections the run left queued because the ratio budget or the unsatisfied-snatch slots are spent.</param>
/// <param name="LimitedBy">
/// Which of those limits left them, and what it comes to, such as "the ratio budget is 0 B (ratio 2.01, min_ratio 2)";
/// null when <paramref name="LeftQueued"/> is 0.
/// </param>
/// <param name="StoppedByMam">
/// Whether MAM refused a request and ended the run before every selection was tried, so how many are left waiting for
/// budget isn't known: <paramref name="LeftQueued"/> counts only those tried before.
/// </param>
public sealed record DownloadRunSummary(int Started, int Failed, int Queued = 0, int LeftQueued = 0, string? LimitedBy = null, bool StoppedByMam = false);

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
    SafetyBuffers safetyBuffers,
    ILogger<DownloadRunner>? logger = null,
    TimeSpan? finishAfterCancel = null)
{
    private readonly ILogger<DownloadRunner> _logger = logger ?? NullLogger<DownloadRunner>.Instance;

    /// <summary>
    /// How long a torrent that is being added gets to finish once the run is cancelled or Fetchshelf stops. Short enough
    /// to end inside a container's stop grace period, so a call that hangs can't keep the process alive until it is
    /// killed mid-torrent.
    /// </summary>
    public static readonly TimeSpan DefaultFinishAfterCancel = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _finishAfterCancel = finishAfterCancel ?? DefaultFinishAfterCancel;

    /// <summary>
    /// Starts queued selections within the account's limits. A failure on one torrent is logged and the run
    /// continues; MAM refusing the account (rate limit, block, 403) ends the run, since every later request would fail too.
    /// A cancellation stops the run before a torrent's file is in hand, when nothing has been spent. Once the file is
    /// fetched, possibly with a wedge, the torrent is added to qBittorrent and recorded before the run stops, unless
    /// that takes longer than <see cref="DefaultFinishAfterCancel"/> after the cancellation.
    /// </summary>
    public async Task<DownloadRunSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await accessGate.AcquireAsync("Download", cancellationToken);

        var pending = await SelectionQueue.PendingAsync(dbContext, cancellationToken);

        if (pending.Count == 0)
        {
            _logger.LogInformation("No selected torrents are waiting to be downloaded");
            return new DownloadRunSummary(0, 0);
        }

        var account = await mamClient.GetAccountStateAsync(cancellationToken);
        var bytesInFlight = await SelectionQueue.InFlightBytesAsync(dbContext, cancellationToken);
        var budget = new DownloadRunBudget(
            account.UnsatLimit, account.UnsatCount, account.Wedges,
            DownloadBudget.ComputeRatioBudgetBytes(account.UploadedBytes, account.DownloadedBytes, bytesInFlight, minRatio));
        _logger.LogInformation(
            "{RatioBudget}", DownloadBudget.DescribeRatioBudget(account.UploadedBytes, account.DownloadedBytes, bytesInFlight, minRatio));

        var (startedCount, failedCount) = (0, 0);
        var (overRatio, noSlot) = (0, 0);
        var stoppedByMam = false;
        // Why selections wait, as it stood when the first of each kind was held back: later torrents change the budget.
        string? ratioLimit = null, slotLimit = null;
        foreach (var selected in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                stoppedByMam = true;
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Download run stopped at {Torrent}; it stays queued for the next run", name);
                throw;
            }
            catch (Exception ex)
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
            if (outcome is DownloadOutcome.LinkStillStaleAfterRefresh or DownloadOutcome.NoLongerOnMam)
            {
                failedCount++;
            }

            if (outcome == DownloadOutcome.OverRatioBudget)
            {
                overRatio++;
                ratioLimit ??= string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"the ratio budget left is {DownloadBudget.FormatBytes(budget.RatioBudgetBytes)}, less than the "
                    + $"{DownloadBudget.FormatBytes(TorrentMetadataSerializer.Deserialize(selected.MetadataJson).TotalSizeBytes)} the first of them needs (min_ratio {minRatio})");
            }
            else if (outcome == DownloadOutcome.NoUnsatSlot)
            {
                noSlot++;
                slotLimit ??= $"no unsatisfied-snatch slot is free ({budget.UnsatCount} of {budget.UnsatLimit} in use, "
                    + $"unsat_buffer {safetyBuffers.UnsatBufferFor(selected.GrabberName)})";
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

        var limits = new[] { ratioLimit, slotLimit }.OfType<string>().ToList();
        return new DownloadRunSummary(
            startedCount, failedCount, pending.Count, overRatio + noSlot, limits.Count == 0 ? null : string.Join("; ", limits), stoppedByMam);
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
            case DownloadOutcome.NoLongerOnMam:
                _logger.LogWarning("Could not download {Torrent}: MAM no longer has it. Removed from the queue; see the Errors page", name);
                break;
            case DownloadOutcome.AlreadyFinishedInClient:
                _logger.LogInformation(
                    "{Torrent} is already finished in qBittorrent; recorded and removed from the queue, its category in qBittorrent left as it is", name);
                break;
            case DownloadOutcome.AlreadyDownloadingInClient:
                _logger.LogInformation(
                    "{Torrent} is already downloading in qBittorrent; recorded and marked as started, its category in qBittorrent left as it is", name);
                break;
            case DownloadOutcome.AlreadyLinked:
                _logger.LogInformation("{Torrent} is already in your library; removed from the queue", name);
                break;
            default:
                _logger.LogInformation("{Torrent}: {Outcome}", name, outcome);
                break;
        }
    }

    /// <summary>The outcome, and whether the torrent file was fetched with a wedge (which spends it on MAM).</summary>
    private async Task<(DownloadOutcome Outcome, bool WedgeSpent)> ProcessOneAsync(
        SelectedTorrentEntity selected, DownloadRunBudget budget, CancellationToken cancellationToken)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(selected.MetadataJson);
        var cost = Enum.Parse<SelectedTorrentCost>(selected.CostDecision);

        if (!DownloadBudget.HasFreeUnsatSlot(budget.UnsatLimit, budget.UnsatCount, safetyBuffers.UnsatBufferFor(selected.GrabberName)))
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

            var decision = DownloadBudget.DecideWedge(cost, wedgesAvailable, safetyBuffers.WedgeBufferFor(selected.GrabberName), isAlreadyFree);
            if (decision == WedgeDecision.Fail)
            {
                return (DownloadOutcome.WedgeUnavailable, false);
            }

            useWedge = decision == WedgeDecision.UseWedge;
        }

        // Fetched without the wedge first: MAM spends a wedge on every download that asks for one, and only the
        // file's info hash shows whether qBittorrent already has the torrent.
        var (torrentFile, removedAs) = await FetchTorrentFileAsync(selected, useWedge: false, cancellationToken);
        if (torrentFile is null)
        {
            return (removedAs!.Value, false);
        }

        var (torrentBytes, hash) = torrentFile.Value;

        var dedupOutcome = await CheckExistingAsync(selected, metadata, hash, cancellationToken);
        if (dedupOutcome is not null)
        {
            return (dedupOutcome.Value, false);
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

        // From here on the torrent is added once its file is in hand, even when the run is cancelled meanwhile: a wedge
        // spent on a file that is then dropped is lost. A cancellation only cuts this short once it is a few seconds old.
        using var finishing = new CancellationTokenSource();
        using var _ = cancellationToken.Register(() => finishing.CancelAfter(_finishAfterCancel));
        cancellationToken = finishing.Token;

        try
        {
            if (useWedge)
            {
                (torrentFile, removedAs) = await FetchTorrentFileAsync(selected, useWedge: true, cancellationToken);
                if (torrentFile is null)
                {
                    return (removedAs!.Value, false);
                }

                torrentBytes = torrentFile.Value.Bytes;
            }

            await AddToQBittorrentAsync(selected, metadata, hash, torrentBytes, cost, useWedge, cancellationToken);
        }
        catch (OperationCanceledException) when (finishing.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Adding {Torrent} was cut short {Seconds:0} s after the run was stopped; it stays queued for the next run{Wedge}",
                metadata.DisplayName(), _finishAfterCancel.TotalSeconds, useWedge ? ". A wedge may have been spent on it" : "");
            throw;
        }

        return (DownloadOutcome.Started, useWedge);
    }

    private async Task<MamTorrentRecord?> FetchCurrentRecordAsync(long mamId, CancellationToken cancellationToken)
    {
        var result = await mamClient.SearchAsync(new MamSearchQuery { Id = mamId }, cancellationToken);
        return result.Data.FirstOrDefault();
    }

    /// <summary>
    /// Fetches the selection's torrent file. MAM answers a stale download link with something that isn't a torrent
    /// file, so only that refreshes the link, by looking the torrent up again; a lookup that finds nothing means MAM no
    /// longer has it. Either way the selection is then taken out of the queue, and the outcome says which. A failure
    /// that says nothing about the link, such as a dropped connection, is thrown as a
    /// <see cref="TorrentFileUnavailableException"/> and leaves the selection queued; an account-wide refusal (403, 406,
    /// 429) and cancellation are thrown as they are, and stop the run.
    /// </summary>
    private async Task<((byte[] Bytes, string Hash)? File, DownloadOutcome? RemovedAs)> FetchTorrentFileAsync(
        SelectedTorrentEntity selected, bool useWedge, CancellationToken cancellationToken)
    {
        try
        {
            if (await TryFetchTorrentFileAsync(selected.DownloadLink, selected.MamId, useWedge, cancellationToken) is { } file)
            {
                return (file, null);
            }

            var current = await FetchCurrentRecordAsync(selected.MamId, cancellationToken);
            if (current is null)
            {
                await RemoveSelectionAsync(selected, NoLongerOnMamError, restorable: false, cancellationToken);
                return (null, DownloadOutcome.NoLongerOnMam);
            }

            if (current.DownloadHash is not { } refreshedLink || refreshedLink == selected.DownloadLink)
            {
                await RemoveSelectionAsync(selected, StaleLinkError, restorable: true, cancellationToken);
                return (null, DownloadOutcome.LinkStillStaleAfterRefresh);
            }

            // Saved straight away, so a failure of the next fetch leaves the selection queued with the working link.
            selected.DownloadLink = refreshedLink;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (await TryFetchTorrentFileAsync(refreshedLink, selected.MamId, useWedge, cancellationToken) is { } refreshed)
            {
                return (refreshed, null);
            }

            await RemoveSelectionAsync(selected, StaleLinkError, restorable: true, cancellationToken);
            return (null, DownloadOutcome.LinkStillStaleAfterRefresh);
        }
        catch (Exception ex) when (IsTemporaryFailure(ex, cancellationToken))
        {
            var wedgeNote = useWedge ? " A wedge may have been spent on the attempt." : "";
            throw new TorrentFileUnavailableException($"{DescribeTemporaryFailure(ex)}.{wedgeNote}", ex);
        }
    }

    /// <summary>The torrent file and its info-hash, or null when MAM answered with something else (a stale link).</summary>
    private async Task<(byte[] Bytes, string Hash)?> TryFetchTorrentFileAsync(
        string downloadLink, long mamId, bool useWedge, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await mamClient.DownloadTorrentFileAsync(downloadLink, mamId, useWedge, cancellationToken);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is >= 400 and < 500)
        {
            // 403, 406 and 429 are thrown as their own exceptions before this; any other refusal is about the link.
            return null;
        }

        return TorrentFileInfoHash.TryCompute(bytes, out var hash) ? (bytes, hash) : null;
    }

    /// <summary>A failure that says nothing about the torrent or its link: no answer from MAM, or a 5xx.</summary>
    private static bool IsTemporaryFailure(Exception ex, CancellationToken cancellationToken) =>
        MamClient.IsNoAnswer(ex, cancellationToken) || ex is HttpRequestException { StatusCode: not null };

    private static string DescribeTemporaryFailure(Exception ex) => ex is HttpRequestException { StatusCode: { } status }
        ? $"MAM answered HTTP {(int)status}"
        : MamClient.DescribeNoAnswer(ex);

    private const string StaleLinkError = "Download link is stale and could not be refreshed.";

    private const string NoLongerOnMamError = "MAM no longer has this torrent (it may have been deleted or merged).";

    /// <summary>
    /// Takes the selection out of the queue and records why on the Errors page; a restorable one keeps what
    /// **Retry download** needs to put it back.
    /// </summary>
    private async Task RemoveSelectionAsync(SelectedTorrentEntity selected, string errorText, bool restorable, CancellationToken cancellationToken)
    {
        selected.RemovedAt = DateTimeOffset.UtcNow;
        var restorePayload = restorable
            ? System.Text.Json.JsonSerializer.Serialize(new GrabberErrorRestorePayload
            {
                MamId = selected.MamId,
                DownloadLink = selected.DownloadLink,
                CostDecision = selected.CostDecision,
                Category = selected.Category,
                TagsJson = selected.TagsJson,
                GrabberName = selected.GrabberName,
                MetadataJson = selected.MetadataJson,
                NormalizedTitleKey = selected.NormalizedTitleKey,
            })
            : null;
        await ErrorLog.RecordAsync(
            dbContext,
            ErrorStage.Grabber,
            selected.MamId.ToString(),
            TorrentMetadataSerializer.Deserialize(selected.MetadataJson).Title,
            errorText,
            restorePayload,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Whether qBittorrent or the library already has this torrent. A torrent qBittorrent has, in whatever category,
    /// is recorded if Fetchshelf did not know it, finished or not, so it shows on the Torrents page and can be linked
    /// once its category or save path matches a library.
    /// </summary>
    private async Task<DownloadOutcome?> CheckExistingAsync(
        SelectedTorrentEntity selected, Fetchshelf.Core.Metadata.TorrentMetadata metadata, string hash, CancellationToken cancellationToken)
    {
        var known = await dbContext.Torrents.AnyAsync(t => t.Id == hash, cancellationToken);
        foreach (var client in qbClients)
        {
            var match = await client.GetTorrentAsync(hash, cancellationToken);
            if (match is null)
            {
                continue;
            }

            if (!known)
            {
                CreateTorrentRecord(hash, metadata, match.State);
            }

            if (match.IsFinished)
            {
                selected.RemovedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                return DownloadOutcome.AlreadyFinishedInClient;
            }

            selected.StartedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return DownloadOutcome.AlreadyDownloadingInClient;
        }

        if (known)
        {
            selected.RemovedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return DownloadOutcome.AlreadyLinked;
        }

        return null;
    }

    private Task ClearGrabberErrorAsync(long mamId, CancellationToken cancellationToken) =>
        ErrorLog.ClearAsync(dbContext, ErrorStage.Grabber, mamId.ToString(), cancellationToken);

    /// <param name="hash">The torrent's info hash.</param>
    /// <param name="metadata">The torrent's metadata from MAM.</param>
    /// <param name="clientStatus">
    /// The torrent's state in qBittorrent when it already has it; null for a torrent just added, which hasn't finished.
    /// </param>
    private void CreateTorrentRecord(string hash, Fetchshelf.Core.Metadata.TorrentMetadata metadata, string? clientStatus = null) =>
        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = hash,
            MamId = metadata.MamId,
            NormalizedTitleKey = Fetchshelf.Core.Metadata.TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
            ClientStatus = clientStatus,
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

/// <summary>A torrent file could not be fetched for a reason that says nothing about the torrent; it stays queued.</summary>
public sealed class TorrentFileUnavailableException(string message, Exception innerException) : Exception(message, innerException);
