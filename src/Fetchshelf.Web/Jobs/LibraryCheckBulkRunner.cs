using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// The Library check page's <b>Delete selected</b> and <b>Relink selected</b>, run by <see cref="BulkOperationWorker"/>
/// like the Torrents page's bulk actions: one finding at a time, each under the library write gate, so the Link and Clean
/// jobs can run between them and a long run can be followed and cancelled. A finding done leaves the list; a book that
/// couldn't be relinked stays, with why noted on it. A finding a newer check replaced is skipped and said so.
/// </summary>
public sealed class LibraryCheckBulkRunner(
    LibraryCheckResults results,
    IReadOnlyList<Library> libraries,
    FetchshelfDbContext dbContext,
    EventStore eventStore,
    LibraryWriteGate writeGate,
    LinkRunner linkRunner,
    FetchshelfSettings settings,
    QBittorrentClientFactory qbittorrentFactory,
    ILogger<LibraryCheckBulkRunner> logger)
{
    public const string Replaced = "skipped: a newer Library check has replaced the finding.";

    public async Task RunAsync(BulkOperation operation, CancellationToken cancellationToken)
    {
        var connections = QBittorrentConnections.Build(settings, qbittorrentFactory);
        var verbs = BulkOperation.Verbs.For(operation.Action, operation.Category);
        var ids = operation.ItemIds;
        var (handled, done) = (0, 0);
        var problems = new List<string>();
        foreach (var id in ids)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var numbered = int.TryParse(id, out var number) ? results.Get(number) : null;
            var label = numbered is null ? id : Label(operation.Action, numbered.Finding);
            operation.Report(handled, label);
            if (numbered is null)
            {
                problems.Add($"Finding {id}: {Replaced}");
                handled++;
                continue;
            }

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
                // A check that ran while this waited for the gate replaced the findings: this one is gone.
                if (results.Get(numbered.Id) is null)
                {
                    problems.Add($"{label}: {Replaced}");
                }
                else
                {
                    dbContext.ChangeTracker.Clear();
                    // Not cancellable: the finding being handled is handled completely.
                    var problem = operation.Action == BulkAction.RelinkFindings
                        ? await RelinkAsync(numbered, connections, operation)
                        : await DeleteAsync(numbered);
                    if (problem is null)
                    {
                        done++;
                    }
                    else
                    {
                        problems.Add($"{label}: {problem}");
                    }
                }
            }

            handled++;
        }

        var state = !cancellationToken.IsCancellationRequested ? BulkOperationState.Finished
            : operation.Cancellation.IsCancellationRequested ? BulkOperationState.Cancelled
            : BulkOperationState.Interrupted;
        operation.End(state, BulkActionRunner.Summary(operation, verbs, ids.Count, handled, done, problems.Count, state), problems);
        logger.LogInformation("Bulk action from the Library check page: {Summary}", operation.Summary);
    }

    /// <summary>What the progress names: a book by its title, a folder or file by its path.</summary>
    private static string Label(BulkAction action, LibraryFinding finding) =>
        action == BulkAction.RelinkFindings && finding.Title is { } title ? title : finding.Path;

    /// <returns>Null when deleted, else why not.</returns>
    private async Task<string?> DeleteAsync(NumberedFinding numbered)
    {
        try
        {
            if (await LibraryCheckDeleter.DeleteAsync(numbered.Finding, libraries, dbContext, eventStore) is { } refusal)
            {
                return refusal;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Library check: could not delete {Path}: {Error}", numbered.Finding.Path, ex.Message);
            return $"could not be deleted: {ex.Message}";
        }

        logger.LogInformation("Library check: deleted {Path}", numbered.Finding.Path);
        results.Remove([numbered.Id]);
        return null;
    }

    /// <returns>Null when relinked, else why not; the finding then stays, with what stops it noted on it.</returns>
    private async Task<string?> RelinkAsync(
        NumberedFinding numbered, IReadOnlyList<(QBittorrentConnection Connection, QBittorrentClient Client)> connections, BulkOperation operation)
    {
        var finding = numbered.Finding;
        if (finding.TorrentId is not { } torrentId)
        {
            return "only books can be relinked.";
        }

        try
        {
            var result = await linkRunner.RelinkAsync(torrentId, connections, operation.ReportPlaced);
            if (result.Succeeded)
            {
                results.Remove([numbered.Id]);
                return null;
            }

            // What stops it now, which may have changed since the check; the message only when that isn't it.
            var (blocker, detail) = await linkRunner.RelinkBlockerAsync(torrentId, connections);
            var note = blocker == RelinkBlocker.None ? result.Message : null;
            results.Update(new Dictionary<int, (LibraryFinding, string?)> { [numbered.Id] = (finding with { Blocker = blocker, BlockerDetail = detail }, note) });
            return note ?? "not relinked; the What to do column says why.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Library check: relinking {Title} failed: {Error}", finding.Title, ex.Message);
            var note = $"Not relinked: {MamErrorDescriber.DescribeAmong(ex, "qBittorrent")}";
            results.Update(new Dictionary<int, (LibraryFinding, string?)> { [numbered.Id] = (finding, note) });
            return note;
        }
    }
}
