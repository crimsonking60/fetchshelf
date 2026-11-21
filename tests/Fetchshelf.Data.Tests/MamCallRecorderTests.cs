using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.MamCalls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>Every call made to MAM, written for the MAM API page and kept for a month.</summary>
public sealed class MamCallRecorderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-mamcalls-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<FetchshelfDbContext> _options;

    public MamCallRecorderTests()
    {
        _options = new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new FetchshelfDbContext(_options);
        DatabaseMigrator.Migrate(context);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private sealed class ContextFactory(DbContextOptions<FetchshelfDbContext> options) : IDbContextFactory<FetchshelfDbContext>
    {
        public FetchshelfDbContext CreateDbContext() => new(options);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private MamCallRecorder Recorder() => new(new ContextFactory(_options), new Clock());

    private static MamCall Call(DateTimeOffset at, int? status = 200, string? failure = null) =>
        new(at, TimeSpan.FromMilliseconds(420.7), "POST", "www.myanonamouse.net", "/tor/js/loadSearchJSONbasic.php", status, failure);

    [Fact]
    public async Task WriteWaitingAsync_WritesEveryCallHandedOver_WithItsCaller()
    {
        var recorder = Recorder();
        recorder.Enqueue(Call(Now.AddMinutes(-2)), "Autograb Freeleech");
        recorder.Enqueue(Call(Now.AddMinutes(-1), status: null, failure: "No answer in time"), null);

        await recorder.WriteWaitingAsync();

        using var context = new FetchshelfDbContext(_options);
        var rows = await context.MamCalls.OrderBy(c => c.StartedAtMs).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal((Now.AddMinutes(-2).ToUnixTimeMilliseconds(), 420, 200, "Autograb Freeleech"), (rows[0].StartedAtMs, rows[0].DurationMs, rows[0].StatusCode, rows[0].Caller));
        Assert.Equal(((int?)null, "No answer in time", (string?)null), (rows[1].StatusCode, rows[1].Failure, rows[1].Caller));
    }

    [Fact]
    public async Task WriteWaitingAsync_DeletesCallsOlderThanAMonth()
    {
        using (var context = new FetchshelfDbContext(_options))
        {
            context.MamCalls.AddRange(
                new MamCallEntity { StartedAtMs = Now.AddDays(-MamCallRecorder.DaysKept - 1).ToUnixTimeMilliseconds(), Method = "GET", Host = "h", Path = "/old" },
                new MamCallEntity { StartedAtMs = Now.AddDays(-MamCallRecorder.DaysKept + 1).ToUnixTimeMilliseconds(), Method = "GET", Host = "h", Path = "/kept" });
            await context.SaveChangesAsync();
        }

        var recorder = Recorder();
        recorder.Enqueue(Call(Now), null);
        await recorder.WriteWaitingAsync();

        using var check = new FetchshelfDbContext(_options);
        Assert.Equal(["/kept", "/tor/js/loadSearchJSONbasic.php"], await check.MamCalls.OrderBy(c => c.StartedAtMs).Select(c => c.Path).ToListAsync());
    }
}
