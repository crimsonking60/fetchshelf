using Microsoft.Extensions.Logging;

namespace Fetchshelf.Core.Jobs;

/// <summary>
/// Writes job runs to the store one after another, away from the jobs themselves. A write that fails, such as when
/// another job holds the database's write lock, is tried again a few times before it is given up on. Jobs neither wait
/// for their records nor fail because of them.
/// </summary>
internal sealed class JobRunWriter(IJobRunStore store, ILogger logger, TimeProvider timeProvider)
{
    /// <summary>The waits before each retry of a failed write: a minute and a half in all.</summary>
    internal static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];

    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    /// <summary>The store's id of one run, once a write has created its row.</summary>
    internal sealed class RunRow
    {
        public long? Id { get; set; }
    }

    public RunRow Started(string jobName, DateTimeOffset startedAt, JobTrigger trigger)
    {
        var row = new RunRow();
        Enqueue(jobName, async () => row.Id ??= await store.StartedAsync(jobName, startedAt, trigger));
        return row;
    }

    /// <summary>
    /// Records how the run ended. If writing its start gave up, its row is created here, so the run is kept all the
    /// same.
    /// </summary>
    public void Finished(
        RunRow row, string jobName, DateTimeOffset startedAt, JobTrigger trigger,
        DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts) =>
        Enqueue(jobName, async () =>
        {
            row.Id ??= await store.StartedAsync(jobName, startedAt, trigger);
            await store.FinishedAsync(row.Id.Value, finishedAt, result, summary, counts);
        });

    /// <summary>Completes once every write queued so far has been made or given up on.</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _tail;
        }
    }

    private void Enqueue(string jobName, Func<Task> write)
    {
        lock (_gate)
        {
            _tail = _tail.ContinueWith(
                _ => WriteAsync(jobName, write), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task WriteAsync(string jobName, Func<Task> write)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await write();
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                logger.LogDebug("Could not record a run of job {JobName} yet ({Error}); trying again", jobName, ex.Message);
                await Task.Delay(RetryDelays[attempt], timeProvider);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not record a run of job {JobName} in the database: {Error}", jobName, ex.Message);
                return;
            }
        }
    }
}
