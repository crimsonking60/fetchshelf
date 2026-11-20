using Fetchshelf.Core.Jobs;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Jobs;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Writes an <see cref="EventKind.UpgradeRun"/> event each time a run of the Upgrade job ends, with its result as the Jobs
/// page shows it, so the Events page keeps what each run did after the Jobs page has moved on to the next.
/// </summary>
public sealed class UpgradeRunEvents(IServiceScopeFactory scopeFactory, ILogger<UpgradeRunEvents> logger, TimeProvider? timeProvider = null)
    : IJobRunListener
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public void RunFinished(JobRunFinished finished)
    {
        if (finished.JobName == JobNames.Upgrade)
        {
            // A listener must not wait (see IJobRunListener): the event is written after the job has gone back to idle.
            _ = WriteAsync(finished);
        }
    }

    /// <summary>Writes the run's event; a failure is logged, as an event can't fail the run it is about.</summary>
    public async Task WriteAsync(JobRunFinished finished)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EventStore>().AppendAsync(new EventEntity
            {
                OccurredAt = finished.Run.FinishedAt ?? _time.GetUtcNow(),
                Kind = EventKind.UpgradeRun,
                RunSummary = JobStatusViewModel.ResultText(finished.Run),
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the Upgrade run's event: {Reason}", ex.Message);
        }
    }
}
