using Fetchshelf.Core.Jobs;
using Fetchshelf.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class SqliteBusyTimeoutTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-busy-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task SaveChanges_WhileAnotherConnectionIsReading_WaitsForTheReaderInsteadOfFailing()
    {
        var options = new DbContextOptionsBuilder<FetchshelfDbContext>().UseFetchshelfSqlite(_dbPath).Options;
        await using (var setup = new FetchshelfDbContext(options))
        {
            DatabaseMigrator.Migrate(setup);
        }

        // A page or a job reading, inside a transaction, while the run is recorded.
        using var reader = new SqliteConnection($"Data Source={_dbPath}");
        reader.Open();
        using (var read = reader.CreateCommand())
        {
            read.CommandText = "BEGIN; SELECT COUNT(*) FROM JobRuns;";
            read.ExecuteScalar();
        }

        var readerDone = Task.Run(async () =>
        {
            await Task.Delay(500);
            using var commit = reader.CreateCommand();
            commit.CommandText = "COMMIT";
            commit.ExecuteNonQuery();
        });

        await using var context = new FetchshelfDbContext(options);
        context.JobRuns.Add(new JobRunEntity { JobName = "Download", StartedAt = DateTimeOffset.UtcNow, Result = JobRunResult.Running, Trigger = JobTrigger.Chained });
        await context.SaveChangesAsync(); // without the busy timeout: "SQLite Error 5: 'database is locked'"
        await readerDone;

        Assert.Equal("Download", context.JobRuns.AsNoTracking().Single().JobName);
    }
}
