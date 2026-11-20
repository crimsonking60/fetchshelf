using System.Diagnostics;
using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>
/// Batches leave the write lock free between them, so another job's save gets in while a long run commits batch after
/// batch. Without the pause it was taken back within microseconds, and a waiting save failed with "database is locked".
/// </summary>
public sealed class SqliteWriteBatchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-write-batch-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private DbContextOptions<FetchshelfDbContext> Options() =>
        new DbContextOptionsBuilder<FetchshelfDbContext>().UseFetchshelfSqlite(_dbPath).Options;

    private async Task<FetchshelfDbContext> MigratedAsync()
    {
        var context = new FetchshelfDbContext(Options());
        DatabaseMigrator.Migrate(context);
        return context;
    }

    private static JobRunEntity Run(string name) =>
        new() { JobName = name, StartedAt = DateTimeOffset.UtcNow, Result = JobRunResult.Running, Trigger = JobTrigger.Chained };

    [Fact]
    public async Task AnotherWriter_GetsInBetweenBatches_ThatFollowEachOtherWithoutAPause()
    {
        await using var setup = await MigratedAsync();

        // A run like the Link job's: batch after batch, each full or old right away, for four seconds.
        using var stop = new CancellationTokenSource();
        var batcher = Task.Run(async () =>
        {
            await using var context = new FetchshelfDbContext(Options());
            var batch = new SqliteWriteBatch(context, maxSize: 1, maxAge: TimeSpan.FromSeconds(2));
            var running = Stopwatch.StartNew();
            while (running.Elapsed < TimeSpan.FromSeconds(4))
            {
                await batch.BeginAsync(CancellationToken.None);
                context.JobRuns.Add(Run("Link"));
                await context.SaveChangesAsync();
                Thread.Sleep(50); // the item's work, with the lock held
                await batch.ItemDoneAsync();
            }

            await batch.CommitAsync();
        });
        await Task.Delay(300);

        // Another job's save, with a busy timeout well inside the run, as a run record or the Seeding job's save is.
        using var other = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        other.Open();
        using (var timeout = other.CreateCommand())
        {
            timeout.CommandText = "PRAGMA busy_timeout = 2000;";
            timeout.ExecuteNonQuery();
        }

        using var insert = other.CreateCommand();
        insert.CommandText = "INSERT INTO JobRuns (JobName, StartedAt, Result, Trigger) VALUES ('Seeding', '2026-11-20 13:26:49+00:00', 0, 0);";
        insert.CommandTimeout = 2;
        var waited = Stopwatch.StartNew();
        insert.ExecuteNonQuery(); // without the pause: "SQLite Error 5: 'database is locked'" after the whole timeout
        waited.Stop();
        await batcher;

        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(1), $"waited {waited.ElapsedMilliseconds} ms");
        Assert.Equal(1, setup.JobRuns.AsNoTracking().Count(r => r.JobName == "Seeding"));
    }

    [Fact]
    public async Task ABatch_CommitsWhenFull_AndBeginIsANoOpWhileOneIsOpen()
    {
        await using var context = await MigratedAsync();
        var batch = new SqliteWriteBatch(context, maxSize: 2, maxAge: TimeSpan.FromMinutes(1));

        await batch.BeginAsync(CancellationToken.None);
        await batch.BeginAsync(CancellationToken.None);
        context.JobRuns.Add(Run("first"));
        await context.SaveChangesAsync();
        await batch.ItemDoneAsync();
        Assert.True(batch.IsOpen);

        context.JobRuns.Add(Run("second"));
        await context.SaveChangesAsync();
        await batch.ItemDoneAsync();

        Assert.False(batch.IsOpen);
        await using var reader = new FetchshelfDbContext(Options());
        Assert.Equal(2, reader.JobRuns.Count());
    }

    [Fact]
    public async Task ANewBatch_LeavesTheLockFreeAMomentAfterTheLastCommit_ButNotWhenTimeHasPassed()
    {
        await using var context = await MigratedAsync();
        var batch = new SqliteWriteBatch(context, maxSize: 1, maxAge: TimeSpan.FromMinutes(1));
        await batch.BeginAsync(CancellationToken.None);
        await batch.CommitAsync();

        var right = Stopwatch.StartNew();
        await batch.BeginAsync(CancellationToken.None);
        right.Stop();
        await batch.CommitAsync();
        await Task.Delay(SqliteWriteBatch.YieldTime * 2);
        var later = Stopwatch.StartNew();
        await batch.BeginAsync(CancellationToken.None);
        later.Stop();
        await batch.CommitAsync();

        Assert.True(right.Elapsed >= SqliteWriteBatch.YieldTime * 0.8, $"right after a commit: {right.ElapsedMilliseconds} ms");
        Assert.True(later.Elapsed < SqliteWriteBatch.YieldTime / 2, $"long after a commit: {later.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ABatchGivenNoYieldTime_BeginsRightAfterTheLastCommit()
    {
        await using var context = await MigratedAsync();
        var batch = new SqliteWriteBatch(context, maxSize: 1, maxAge: TimeSpan.FromMinutes(1), yieldTime: TimeSpan.Zero);
        await batch.BeginAsync(CancellationToken.None);
        await batch.CommitAsync();

        var right = Stopwatch.StartNew();
        await batch.BeginAsync(CancellationToken.None);
        right.Stop();
        await batch.CommitAsync();

        Assert.True(right.Elapsed < SqliteWriteBatch.YieldTime / 2, $"right after a commit: {right.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task CommitWithoutABatch_DoesNothing()
    {
        await using var context = await MigratedAsync();
        var batch = new SqliteWriteBatch(context, maxSize: 1, maxAge: TimeSpan.FromMinutes(1));

        await batch.CommitAsync();
        await batch.ItemDoneAsync();

        Assert.False(batch.IsOpen);
    }
}
