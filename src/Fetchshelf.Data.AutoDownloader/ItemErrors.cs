using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// The error rows of a run that works through items one at a time, searching MAM for each: a followed series or author,
/// a Hardcover list's book, a library copy. An item's row is cleared once its work succeeds and recorded when it fails,
/// and what a failed item changed is discarded, so it isn't saved along with the next one. MAM refusing the account ends
/// the run instead: every later item would fail the same way, so no item gets an error of its own. A dry run writes
/// nothing.
/// </summary>
internal sealed class ItemErrors
{
    private readonly FetchshelfDbContext _dbContext;
    private readonly ErrorStage _stage;
    private readonly bool _dryRun;
    private readonly HashSet<string> _keys;

    private ItemErrors(FetchshelfDbContext dbContext, ErrorStage stage, bool dryRun, HashSet<string> keys) =>
        (_dbContext, _stage, _dryRun, _keys) = (dbContext, stage, dryRun, keys);

    /// <summary>Reads which items have an error row at <paramref name="stage"/>, so a run clears only the rows there are.</summary>
    public static async Task<ItemErrors> LoadAsync(FetchshelfDbContext dbContext, ErrorStage stage, bool dryRun, CancellationToken cancellationToken) =>
        new(dbContext, stage, dryRun, dryRun ? [] : await ErrorLog.KeysAsync(dbContext, stage, cancellationToken));

    /// <summary>
    /// Runs one item's work and saves what it changed, with its error row cleared or recorded. Returns null when the work
    /// succeeded, else the exception it failed with, for the caller to count and log.
    /// </summary>
    /// <param name="key">The item's error key.</param>
    /// <param name="title">The title its error row shows.</param>
    /// <param name="work">The item's work.</param>
    /// <param name="markFailed">
    /// What is kept of a failed item along with its error, such as when it was last searched, so an item that keeps
    /// failing waits its turn instead of taking a search from the others on every run. Not called in a dry run.
    /// </param>
    /// <param name="cancellationToken">Cancels the run: a cancellation is rethrown, never recorded as the item's error.</param>
    public async Task<Exception?> RunAsync(string key, string? title, Func<Task> work, Action? markFailed, CancellationToken cancellationToken)
    {
        try
        {
            await work();
            if (!_dryRun)
            {
                if (_keys.Remove(key))
                {
                    await ErrorLog.ClearAsync(_dbContext, _stage, key, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
        catch (Exception ex) when (ex is MamAccountBlockedException or MamRateLimitedException or MamForbiddenException)
        {
            _dbContext.DiscardPendingChanges();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _dbContext.DiscardPendingChanges();
            if (!_dryRun)
            {
                markFailed?.Invoke();
                await ErrorLog.RecordAsync(_dbContext, _stage, key, title, ex.Message, cancellationToken: cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _keys.Add(key);
            }

            return ex;
        }
    }
}
