using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>`budget_spent`: sent when selections start waiting for budget, remembered in the database.</summary>
public sealed class BudgetSpentWatcherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Reason = "the ratio budget left is 0 B, less than the 1.0 GiB the first of them needs (min_ratio 2)";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-budget-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<FetchshelfDbContext> _options;

    public BudgetSpentWatcherTests()
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

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private (BudgetSpentWatcher Watcher, Notifier Notifier) Create(params NotificationEvent[] wanted)
    {
        var notifier = new Notifier([new NotifyTarget("Phone", "https://x.test/a", NotificationFormat.Json, null, wanted.ToHashSet())]);
        var time = new FixedTime(Now);
        return (new BudgetSpentWatcher(new NotificationStateStore(new ContextFactory(_options), time), notifier, time), notifier);
    }

    private static int Sent(Notifier notifier)
    {
        var count = 0;
        while (notifier.Reader.TryRead(out _))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public async Task SelectionsWaitingAfterARunThatLeftNone_AreToldOnce()
    {
        var (watcher, notifier) = Create(NotificationEvent.BudgetSpent);

        await watcher.CheckAsync(3, Reason);
        await watcher.CheckAsync(3, Reason);

        Assert.Equal(1, Sent(notifier));
    }

    [Fact]
    public async Task ARunThatFailedInBetween_DoesNotMakeTheNextLookNew()
    {
        var (watcher, notifier) = Create(NotificationEvent.BudgetSpent);

        await watcher.CheckAsync(5, Reason);
        // A failed or cancelled Download run never reaches the budget, so it isn't checked at all.
        await watcher.CheckAsync(5, Reason);

        Assert.Equal(1, Sent(notifier));
    }

    [Fact]
    public async Task AfterAllStarted_WaitingAgainIsToldAgain()
    {
        var (watcher, notifier) = Create(NotificationEvent.BudgetSpent);

        await watcher.CheckAsync(2, Reason);
        await watcher.CheckAsync(0, null);
        await watcher.CheckAsync(1, Reason);

        Assert.Equal(2, Sent(notifier));
    }

    [Fact]
    public async Task ARestart_KeepsWhatTheLastRunLeft()
    {
        var (before, notifierBefore) = Create(NotificationEvent.BudgetSpent);
        await before.CheckAsync(4, Reason);
        Assert.Equal(1, Sent(notifierBefore));

        var (after, notifierAfter) = Create(NotificationEvent.BudgetSpent); // a new process, the same database
        await after.CheckAsync(4, Reason);

        Assert.Equal(0, Sent(notifierAfter));
    }

    [Fact]
    public async Task NoBlockWantingIt_StoresNothing()
    {
        var (watcher, _) = Create(NotificationEvent.Grabbed);

        await watcher.CheckAsync(3, Reason);

        using var context = new FetchshelfDbContext(_options);
        Assert.Empty(context.NotificationStates);
    }
}
