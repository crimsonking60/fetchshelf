using Fetchshelf.Core.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class JobRunStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-job-runs-{Guid.NewGuid():N}.db");
    private readonly JobRunStore _store;

    public JobRunStoreTests()
    {
        var factory = new ContextFactory(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        using (var context = factory.CreateDbContext())
        {
            DatabaseMigrator.Migrate(context);
        }

        _store = new JobRunStore(factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task FinishedAsync_ARun_IsReadBackAsItsJobsLastRun()
    {
        var id = await _store.StartedAsync("Link", Start, JobTrigger.Startup);
        await _store.FinishedAsync(
            id, Start.AddSeconds(42), JobRunResult.CompletedWithErrors, "2 torrents linked; 1 could not be linked",
            new Dictionary<string, long> { ["linked"] = 2, ["failed"] = 1 });

        var run = (await _store.LastRunsAsync())["Link"];

        Assert.Equal(
            (Start, Start.AddSeconds(42), JobRunResult.CompletedWithErrors, JobTrigger.Startup, "2 torrents linked; 1 could not be linked"),
            (run.StartedAt, run.FinishedAt, run.Result, run.Trigger, run.Summary));
        Assert.Equal(new Dictionary<string, long> { ["linked"] = 2, ["failed"] = 1 }, run.Counts);
    }

    [Fact]
    public async Task LastRunsAsync_SeveralRunsOfSeveralJobs_GivesTheNewestOfEach()
    {
        for (var i = 0; i < 3; i++)
        {
            var link = await _store.StartedAsync("Link", Start.AddMinutes(i), JobTrigger.Schedule);
            await _store.FinishedAsync(link, Start.AddMinutes(i), JobRunResult.Success, $"run {i}", null);
        }

        await _store.StartedAsync("Download", Start, JobTrigger.Chained);

        var lastRuns = await _store.LastRunsAsync();

        Assert.Equal(2, lastRuns.Count);
        Assert.Equal("run 2", lastRuns["Link"].Summary);
        Assert.Equal(JobRunResult.Running, lastRuns["Download"].Result);
    }

    [Fact]
    public async Task InterruptUnfinishedAsync_ARunStillRunning_IsMarkedInterruptedAtTheGivenTime()
    {
        await _store.StartedAsync("Download", Start, JobTrigger.Chained);
        var finished = await _store.StartedAsync("Link", Start, JobTrigger.Schedule);
        await _store.FinishedAsync(finished, Start.AddSeconds(5), JobRunResult.Success, null, null);

        await _store.InterruptUnfinishedAsync(Start.AddHours(1), "stopped");

        var lastRuns = await _store.LastRunsAsync();
        Assert.Equal((JobRunResult.Interrupted, Start.AddHours(1), "stopped"), (lastRuns["Download"].Result, lastRuns["Download"].FinishedAt, lastRuns["Download"].Summary));
        Assert.Equal(JobRunResult.Success, lastRuns["Link"].Result);
    }

    [Fact]
    public async Task FinishedAsync_MoreRunsThanKept_KeepsOnlyTheNewestOfThatJob()
    {
        var other = await _store.StartedAsync("Clean", Start, JobTrigger.Chained);
        await _store.FinishedAsync(other, Start, JobRunResult.Success, "kept", null);
        for (var i = 0; i < JobRunStore.RunsKeptPerJob + 5; i++)
        {
            var id = await _store.StartedAsync("Link", Start.AddMinutes(i), JobTrigger.Schedule);
            await _store.FinishedAsync(id, Start.AddMinutes(i), JobRunResult.Success, $"run {i}", null);
        }

        await using var context = new FetchshelfDbContext(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var linkRuns = await context.JobRuns.Where(r => r.JobName == "Link").OrderBy(r => r.Id).Select(r => r.Summary).ToListAsync();
        Assert.Equal(JobRunStore.RunsKeptPerJob, linkRuns.Count);
        Assert.Equal("run 5", linkRuns[0]);
        Assert.Equal($"run {JobRunStore.RunsKeptPerJob + 4}", linkRuns[^1]);
        Assert.Equal("kept", context.JobRuns.Single(r => r.JobName == "Clean").Summary);
    }

    private sealed class ContextFactory(DbContextOptions<FetchshelfDbContext> options) : IDbContextFactory<FetchshelfDbContext>
    {
        public FetchshelfDbContext CreateDbContext() => new(options);
    }
}
