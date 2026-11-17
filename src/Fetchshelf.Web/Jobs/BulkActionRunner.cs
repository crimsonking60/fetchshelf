using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Pages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Works through one <see cref="BulkOperation"/>: one torrent at a time, each under <see cref="LibraryWriteGate"/>, so it
/// never works on files at the same time as the Link or Clean job. A torrent that is refused or fails is reported, and
/// the rest still run. Cancelling stops it between torrents: the one being handled is handled completely.
/// </summary>
public sealed class BulkActionRunner(
    FetchshelfDbContext dbContext,
    LinkRunner linkRunner,
    Cleaner cleaner,
    LibraryWriteGate writeGate,
    FetchshelfSettings settings,
    QBittorrentClientFactory qbittorrentFactory,
    ILogger<BulkActionRunner> logger)
{
    /// <summary>
    /// Runs <paramref name="operation"/> and ends it with its summary: finished, or, when
    /// <paramref name="cancellationToken"/> stops it, cancelled (its own <b>Cancel</b>) or interrupted (Fetchshelf stopping).
    /// </summary>
    public async Task RunAsync(BulkOperation operation, CancellationToken cancellationToken)
    {
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        var ids = operation.TorrentIds;
        var titles = await dbContext.Torrents.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Title, cancellationToken);
        var verbs = BulkOperation.Verbs.For(operation.Action, operation.Category);
        var (handled, done) = (0, 0);
        var problems = new List<string>();
        foreach (var id in ids.Where(titles.ContainsKey))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            operation.Report(handled, titles[id]);
            IDisposable gate;
            try
            {
                gate = await writeGate.AcquireAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            using (gate)
            {
                // What another job changed since the last torrent is read afresh.
                dbContext.ChangeTracker.Clear();
                try
                {
                    // Not cancellable: the torrent being handled is handled completely.
                    var (succeeded, message) = await RunOneAsync(operation, id, connections);
                    if (succeeded)
                    {
                        done++;
                    }
                    else
                    {
                        problems.Add($"{titles[id]}: {message}");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{Action} {TorrentId} failed", verbs.Failing, id);
                    // What the failed attempt changed must not be saved along with the next torrent.
                    dbContext.DiscardPendingChanges();
                    problems.Add($"{titles[id]}: {verbs.Failing} failed: {MamErrorDescriber.DescribeAmong(ex, "MAM, qBittorrent or Audiobookshelf")}");
                }
            }

            handled++;
        }

        var state = !cancellationToken.IsCancellationRequested ? BulkOperationState.Finished
            : operation.Cancellation.IsCancellationRequested ? BulkOperationState.Cancelled
            : BulkOperationState.Interrupted;
        operation.End(state, Summary(verbs, titles.Count, handled, done, problems.Count, state), problems);
        logger.LogInformation("Bulk action from the Torrents page: {Summary}", operation.Summary);
    }

    private async Task<(bool Succeeded, string Message)> RunOneAsync(
        BulkOperation operation, string id, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections)
    {
        switch (operation.Action)
        {
            case BulkAction.Relink:
                var relinked = await linkRunner.RelinkAsync(id, connections);
                return (relinked.Succeeded, relinked.Message);
            case BulkAction.Unlink:
                var unlinked = await cleaner.UnlinkAsync(id, connections);
                return (unlinked.Succeeded, unlinked.Message);
            case BulkAction.Refresh:
                return await linkRunner.RefreshAsync(id) == RefreshOutcome.NotFoundOnMam
                    ? (false, "MAM did not find it, so nothing was refreshed.")
                    : (true, string.Empty);
            case BulkAction.Clean:
                // Nothing to retire is no failure: the torrent simply has no other copy linked.
                await cleaner.CleanOneAsync(id, connections);
                return (true, string.Empty);
            default:
                var client = await QBittorrentConnectionResolver.ResolveAsync(connections.Select(c => c.Client).ToList(), id);
                if (client is null)
                {
                    return (false, "qBittorrent doesn't have it.");
                }

                await client.SetCategoryAsync(id, operation.Category!);
                // Shown in the list straight away; the Link job keeps it in step with qBittorrent from now on.
                var torrent = await dbContext.Torrents.FirstAsync(t => t.Id == id);
                torrent.QBittorrentCategory = operation.Category;
                await dbContext.SaveChangesAsync();
                return (true, string.Empty);
        }
    }

    /// <summary>"Relinked 18 of 23 torrents.", and, when it stopped early, how far it got. The problems are listed apart.</summary>
    public static string Summary(BulkOperation.Verbs verbs, int total, int handled, int done, int problems, BulkOperationState state)
    {
        string Torrents(int count) => count == 1 ? "1 torrent" : $"{count} torrents";
        var line = state == BulkOperationState.Finished && problems == 0
            ? $"{verbs.Done} {Torrents(total)}."
            : $"{verbs.Done} {done} of {Torrents(total)}.";
        if (state != BulkOperationState.Finished)
        {
            line += $" {(state == BulkOperationState.Cancelled ? "Cancelled" : "Fetchshelf stopped")} after {handled}; the other {total - handled} were left as they were.";
        }

        return line;
    }
}
