using Fetchshelf.Clients.Mam;
using Fetchshelf.Data.Account;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>One snapshot of the account a UTC day, from the account reads the jobs make.</summary>
public sealed class AccountSnapshotRecorderTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-snapshots-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<FetchshelfDbContext> _options;
    private DateTimeOffset _now = Morning;

    public AccountSnapshotRecorderTests()
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

    private sealed class BrokenFactory : IDbContextFactory<FetchshelfDbContext>
    {
        public FetchshelfDbContext CreateDbContext() => throw new InvalidOperationException("no database");
    }

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }

    private AccountSnapshotRecorder Recorder() => new(new ContextFactory(_options), new Clock(() => _now));

    private static MamAccountSnapshot Read(DateTimeOffset at, double ratio = 2.5, long wedges = 10, double? points = 1000) => new(
        new MamAccountState
        {
            Ratio = ratio,
            UploadedBytes = 5e12,
            DownloadedBytes = 2e12,
            Wedges = wedges,
            Uid = 1,
            UnsatCount = 12,
            UnsatLimit = 200,
            BonusPoints = points,
            ClassName = "Power User",
        },
        at);

    private List<Entities.AccountSnapshotEntity> Rows()
    {
        using var context = new FetchshelfDbContext(_options);
        return context.AccountSnapshots.AsNoTracking().OrderBy(s => s.Day).ToList();
    }

    [Fact]
    public async Task RecordAsync_FirstReadOfADay_AddsTheDaysRow()
    {
        await Recorder().RecordAsync(Read(Morning));

        var row = Assert.Single(Rows());
        Assert.Equal("2026-11-17", row.Day);
        Assert.Equal(Morning, row.TakenAt);
        Assert.Equal((2.5, 5_000_000_000_000L, 2_000_000_000_000L, 10L, 12L, 200L, 1000.0, "Power User"),
            (row.Ratio, row.UploadedBytes, row.DownloadedBytes, row.Wedges, row.UnsatCount, row.UnsatLimit, row.BonusPoints, row.ClassName));
    }

    [Fact]
    public async Task RecordAsync_LaterReadThatDay_ReplacesTheRow()
    {
        var recorder = Recorder();
        await recorder.RecordAsync(Read(Morning));

        await recorder.RecordAsync(Read(Morning.AddHours(3), ratio: 2.6, wedges: 9));

        var row = Assert.Single(Rows());
        Assert.Equal((Morning.AddHours(3), 2.6, 9L), (row.TakenAt, row.Ratio, row.Wedges));
    }

    [Fact]
    public async Task RecordAsync_ReadWithinTenMinutesOfTheRow_IsSkipped()
    {
        var recorder = Recorder();
        await recorder.RecordAsync(Read(Morning));

        await recorder.RecordAsync(Read(Morning.AddMinutes(9), ratio: 9));
        await recorder.RecordAsync(Read(Morning.AddMinutes(-30), ratio: 8));

        Assert.Equal((Morning, 2.5), (Assert.Single(Rows()).TakenAt, Assert.Single(Rows()).Ratio));
    }

    [Fact]
    public async Task RecordAsync_ANewUtcDay_AddsANewRow()
    {
        var recorder = Recorder();
        await recorder.RecordAsync(Read(new DateTimeOffset(2026, 11, 17, 23, 59, 0, TimeSpan.Zero)));

        // 00:30 on the 18th UTC is still the 17th west of Greenwich: the UTC day decides.
        await recorder.RecordAsync(Read(new DateTimeOffset(2026, 11, 17, 19, 30, 0, TimeSpan.FromHours(-5))));

        Assert.Equal(["2026-11-17", "2026-11-18"], Rows().Select(r => r.Day));
    }

    [Fact]
    public async Task RecordAsync_DeletesDaysOlderThanTwoYears_OnceADay()
    {
        using (var context = new FetchshelfDbContext(_options))
        {
            foreach (var day in new[] { "2024-11-16", "2024-11-17", "2025-06-01" })
            {
                context.AccountSnapshots.Add(new Entities.AccountSnapshotEntity { Day = day, TakenAt = Morning.AddYears(-1) });
            }

            context.SaveChanges();
        }

        var recorder = Recorder();
        await recorder.RecordAsync(Read(Morning));

        Assert.Equal(["2024-11-17", "2025-06-01", "2026-11-17"], Rows().Select(r => r.Day));

        // Pruned already today: a row that has become too old meanwhile waits for tomorrow.
        using (var context = new FetchshelfDbContext(_options))
        {
            context.AccountSnapshots.Add(new Entities.AccountSnapshotEntity { Day = "2023-01-01", TakenAt = Morning.AddYears(-3) });
            context.SaveChanges();
        }

        await recorder.RecordAsync(Read(Morning.AddHours(1)));
        Assert.Contains("2023-01-01", Rows().Select(r => r.Day));

        _now = Morning.AddDays(1);
        await recorder.RecordAsync(Read(Morning.AddDays(1)));
        Assert.DoesNotContain("2023-01-01", Rows().Select(r => r.Day));
    }

    [Fact]
    public async Task RecordAsync_DatabaseFails_IsLoggedNotThrown()
    {
        var recorder = new AccountSnapshotRecorder(new BrokenFactory(), TimeProvider.System);

        Assert.Null(await Record.ExceptionAsync(() => recorder.RecordAsync(Read(Morning))));
    }

    [Fact]
    public async Task RunAsync_RecordsWhatIsEnqueued()
    {
        var recorder = Recorder();
        using var stop = new CancellationTokenSource();
        var loop = recorder.RunAsync(stop.Token);

        recorder.Enqueue(Read(Morning));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Rows().Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await stop.CancelAsync();
        await loop;
        Assert.Single(Rows());
    }
}
