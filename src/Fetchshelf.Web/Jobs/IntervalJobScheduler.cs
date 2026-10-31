using Fetchshelf.Core.Jobs;
using Microsoft.Extensions.Hosting;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Triggers a registered job on its configured interval. An interval of 0 means the job only runs when
/// triggered from the UI. On-demand triggering (the Jobs page's "Run now") calls
/// <see cref="IJobRegistry.TriggerAsync"/> directly and needs no part of this class.
/// </summary>
public sealed class IntervalJobScheduler(IJobRegistry jobRegistry, string jobName, TimeSpan interval) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                jobRegistry.SetNextScheduledRun(jobName, DateTimeOffset.UtcNow + interval);
                await Task.Delay(interval, stoppingToken);
                await jobRegistry.TriggerAsync(jobName, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
