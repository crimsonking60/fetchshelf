namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Runs the Torrents page's bulk actions from <see cref="BulkOperationQueue"/>, one at a time, in order, each in a scope
/// of its own and not tied to the request that queued it: leaving the page doesn't stop one.
/// </summary>
public sealed class BulkOperationWorker(BulkOperationQueue queue, IServiceScopeFactory scopeFactory, ILogger<BulkOperationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var operation in queue.Pending.ReadAllAsync(stoppingToken))
            {
                if (!operation.TryStart())
                {
                    continue; // cancelled while it waited
                }

                using var stop = CancellationTokenSource.CreateLinkedTokenSource(operation.Cancellation.Token, stoppingToken);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<BulkActionRunner>().RunAsync(operation, stop.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Bulk action {Operation} failed", operation.Describe());
                    operation.End(BulkOperationState.Finished, $"{operation.Describe()} failed.", [ex.Message]);
                }

                queue.Ended(operation);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Fetchshelf is stopping. Operations still waiting are forgotten with the rest of memory.
        }
    }
}
