using System.Globalization;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Pages;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// The ticked finds' <b>Download</b>, <b>Wedge download</b>, <b>Dismiss</b> and <b>Undo dismiss</b>, of a follow's page or
/// the To look at page, run by <see cref="BulkOperationWorker"/> like the Torrents page's bulk actions: one find at a time,
/// each as its own button does, so a long run can be followed and cancelled. A download waits for MAM while another job
/// uses it, and a MAM failure stops the rest, as they would most likely fail the same way. Once books are selected, the
/// Download job is started, as after a single Download.
/// </summary>
public sealed class FindsBulkRunner(
    FetchshelfDbContext dbContext,
    IServiceProvider services,
    FetchshelfSettings settings,
    IJobRegistry jobRegistry,
    FormatPreferenceLists formatPreferenceLists,
    MamClientOptions mamOptions,
    ILogger<FindsBulkRunner> logger)
{
    public async Task RunAsync(BulkOperation operation, CancellationToken cancellationToken)
    {
        var verbs = BulkOperation.Verbs.For(operation.Action, operation.Category);
        var ids = operation.ItemIds.Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToList();
        var titles = await dbContext.FollowFinds.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Title, cancellationToken);
        var (handled, done) = (0, 0);
        var problems = new List<string>();
        if (services.GetService<FollowService>() is not { } followService)
        {
            operation.End(BulkOperationState.Finished, $"{operation.Describe()}: nothing was done, as there is no [follow] section.", []);
            return;
        }

        var stopped = false;
        foreach (var id in ids)
        {
            if (cancellationToken.IsCancellationRequested || stopped)
            {
                break;
            }

            var title = titles.GetValueOrDefault(id);
            operation.Report(handled, title);
            if (title is null)
            {
                problems.Add($"Find {id}: no longer listed.");
                handled++;
                continue;
            }

            operation.Handling();
            try
            {
                var problem = operation.Action switch
                {
                    BulkAction.DismissFinds => await DismissAsync(followService, id),
                    BulkAction.UndoDismissFinds => await followService.UndoDismissAsync(id, CancellationToken.None) ? null : "it was not dismissed.",
                    _ => await DownloadAsync(followService, id, operation.Action == BulkAction.WedgeDownloadFinds, cancellationToken),
                };
                if (problem is null)
                {
                    done++;
                }
                else
                {
                    problems.Add(problem.StartsWith(title, StringComparison.Ordinal) ? problem : $"{title}: {problem}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Bulk action on follow find {FindId} failed", id);
                problems.Add($"{title}: {verbs.Failing.ToLowerInvariant()} failed: {MamErrorDescriber.Describe(ex, mamOptions.Timeout)}");
                // Most likely MAM: the rest would fail the same way.
                stopped = operation.Action is BulkAction.DownloadFinds or BulkAction.WedgeDownloadFinds;
            }

            dbContext.ChangeTracker.Clear();
            handled++;
        }

        var state = !cancellationToken.IsCancellationRequested ? BulkOperationState.Finished
            : operation.Cancellation.IsCancellationRequested ? BulkOperationState.Cancelled
            : BulkOperationState.Interrupted;
        var summary = BulkActionRunner.Summary(operation, verbs, ids.Count, handled, done, problems.Count, state);
        if (stopped && handled < ids.Count)
        {
            summary += $" The other {ids.Count - handled} were left as they were.";
        }

        if (operation.Action is BulkAction.DownloadFinds or BulkAction.WedgeDownloadFinds && done > 0)
        {
            summary += StartDownload() ? " The Download job is starting them now." : " They start at the next Download run.";
        }

        operation.End(state, summary, problems);
        logger.LogInformation("Bulk action from a list of follow finds: {Summary}", operation.Summary);
    }

    private static async Task<string?> DismissAsync(FollowService followService, int id)
    {
        // Not cancellable: the find being handled is handled completely.
        await followService.DismissAsync(id, CancellationToken.None);
        return null;
    }

    /// <returns>Null when selected, else why not.</returns>
    private async Task<string?> DownloadAsync(FollowService followService, int id, bool wedge, CancellationToken cancellationToken)
    {
        var result = await followService.DownloadAsync(id, wedge, cancellationToken, waitForMam: true);
        return result switch
        {
            { Outcome: { } outcome } when outcome.IsSelected() => null,
            { Outcome: { } other, Metadata: { } metadata } => SelectionOutcomeMessages.Describe(other, metadata, formatPreferenceLists, downloadStarted: false),
            _ => result.Problem ?? "not selected.",
        };
    }

    private bool StartDownload()
    {
        if (!settings.DownloadAfterSelection || jobRegistry.Get(JobNames.Download) is null)
        {
            return false;
        }

        _ = jobRegistry.TriggerOrQueueAsync(JobNames.Download, JobTrigger.Manual, CancellationToken.None);
        return true;
    }
}
