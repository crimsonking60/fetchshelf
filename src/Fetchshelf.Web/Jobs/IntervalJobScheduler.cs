using Fetchshelf.Core.Jobs;
using Microsoft.Extensions.Hosting;

namespace Fetchshelf.Web.Jobs;

/// <summary>
/// Triggers a registered job on its configured interval. An interval of 0 means the job only runs when
/// triggered from the UI. On-demand triggering (the Jobs page's "Run now") calls
/// <see cref="IJobRegistry.TriggerAsync"/> directly and needs no part of this class.
/// </summary>
/// <param name="jobRegistry">The registry the job is registered in, with its last run already loaded.</param>
/// <param name="jobName">The job to trigger.</param>
/// <param name="interval">How long after one run ends the next one starts.</param>
/// <param name="startupDelay">
/// How long after start a job that is due, or has never run, first runs. Each job gets its own, so they start one
/// after another. See <see cref="FirstRunPlanner"/>.
/// </param>
/// <param name="timeProvider">The clock; the system clock if null.</param>
public sealed class IntervalJobScheduler(
    IJobRegistry jobRegistry, string jobName, TimeSpan interval, TimeSpan startupDelay, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            return;
        }

        var first = FirstRunPlanner.Plan(_timeProvider.GetUtcNow(), jobRegistry.Get(jobName)?.LastRun?.StartedAt, interval, startupDelay);
        var nextRun = first.At;
        var trigger = first.IsStartup ? JobTrigger.Startup : JobTrigger.Schedule;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                jobRegistry.SetNextScheduledRun(jobName, nextRun);
                var delay = nextRun - _timeProvider.GetUtcNow();
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _timeProvider, stoppingToken);
                }

                await jobRegistry.TriggerAsync(jobName, trigger, stoppingToken);
                trigger = JobTrigger.Schedule;
                nextRun = _timeProvider.GetUtcNow() + interval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
