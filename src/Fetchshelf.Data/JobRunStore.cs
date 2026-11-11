using System.Text.Json;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// Keeps job runs in the JobRuns table. Each call uses its own context: the registry is a singleton, and a run must
/// be recorded even when the job's own scope failed.
/// </summary>
public sealed class JobRunStore(IDbContextFactory<FetchshelfDbContext> contextFactory) : IJobRunStore
{
    /// <summary>
    /// How many runs of one job are kept: days of history for a job that runs every few minutes, months for a daily
    /// one, at a few kilobytes per job.
    /// </summary>
    public const int RunsKeptPerJob = 200;

    public async Task<long> StartedAsync(string jobName, DateTimeOffset startedAt, JobTrigger trigger)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var run = new JobRunEntity { JobName = jobName, StartedAt = startedAt, Result = JobRunResult.Running, Trigger = trigger };
        context.JobRuns.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    public async Task FinishedAsync(
        long runId, DateTimeOffset finishedAt, JobRunResult result, string? summary, IReadOnlyDictionary<string, long>? counts)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var run = await context.JobRuns.FirstOrDefaultAsync(r => r.Id == runId);
        if (run is null)
        {
            return;
        }

        run.FinishedAt = finishedAt;
        run.Result = result;
        run.Summary = summary;
        run.CountsJson = counts is null ? null : JsonSerializer.Serialize(counts);
        await context.SaveChangesAsync();

        // Ids grow with every run, so the newest runs are the highest ids.
        var oldestKept = await context.JobRuns
            .Where(r => r.JobName == run.JobName)
            .OrderByDescending(r => r.Id)
            .Skip(RunsKeptPerJob - 1)
            .Select(r => (long?)r.Id)
            .FirstOrDefaultAsync();
        if (oldestKept is { } cutoff)
        {
            await context.JobRuns.Where(r => r.JobName == run.JobName && r.Id < cutoff).ExecuteDeleteAsync();
        }
    }

    public async Task InterruptUnfinishedAsync(string summary)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.JobRuns
            .Where(r => r.Result == JobRunResult.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Result, JobRunResult.Interrupted)
                .SetProperty(r => r.Summary, summary));
    }

    public async Task<IReadOnlyDictionary<string, JobRunRecord>> LastRunsAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var newestIds = await context.JobRuns.GroupBy(r => r.JobName).Select(g => g.Max(r => r.Id)).ToListAsync();
        var runs = await context.JobRuns.AsNoTracking().Where(r => newestIds.Contains(r.Id)).ToListAsync();
        return runs.ToDictionary(r => r.JobName, ToRecord);
    }

    public static JobRunRecord ToRecord(JobRunEntity run) =>
        new(run.StartedAt, run.Result, run.Summary, run.FinishedAt, run.Trigger, ReadCounts(run.CountsJson));

    public static IReadOnlyDictionary<string, long>? ReadCounts(string? countsJson) =>
        countsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, long>>(countsJson);
}
