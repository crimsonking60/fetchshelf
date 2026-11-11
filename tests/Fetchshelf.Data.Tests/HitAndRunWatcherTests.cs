using System.Globalization;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Notifications;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

/// <summary>`hit_and_run`: each account read compared with the count kept in the database.</summary>
public sealed class HitAndRunWatcherTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"fetchshelf-hnr-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<FetchshelfDbContext> _options;

    public HitAndRunWatcherTests()
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

    private static NotifyTarget Wanting(params NotificationEvent[] events) =>
        new("Phone", "https://x.test/a", NotificationFormat.Json, null, events.ToHashSet());

    /// <summary>A watcher with a notifier that wants these events, so what it publishes can be read from the notifier's queue.</summary>
    private (HitAndRunWatcher Watcher, Notifier Notifier) CreateWatcher(params NotificationEvent[] wanted)
    {
        var notifier = new Notifier([Wanting(wanted)]);
        return (new HitAndRunWatcher(new ContextFactory(_options), notifier, new FixedTime(Now)), notifier);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static MamAccountState Account(long? hitAndRuns) => new()
    {
        Ratio = 2,
        UploadedBytes = 1,
        DownloadedBytes = 1,
        Wedges = 0,
        Uid = 1,
        UnsatCount = 0,
        UnsatLimit = 20,
        SnatchKinds = hitAndRuns is { } count
            ? [new MamSnatchKind("inactHnr", "Not Seeding - H&R - Not Yet Satisfied", count, null), new MamSnatchKind("unsat", "Unsatisfied", 0, null)]
            : [new MamSnatchKind("unsat", "Unsatisfied", 0, null)],
    };

    private void Store(long count)
    {
        using var context = new FetchshelfDbContext(_options);
        context.NotificationStates.Add(new NotificationStateEntity
        {
            Key = AccountNotificationRules.HitAndRunStateKey,
            Value = count.ToString(CultureInfo.InvariantCulture),
            UpdatedAt = Now.AddDays(-1),
        });
        context.SaveChanges();
    }

    private string? Stored()
    {
        using var context = new FetchshelfDbContext(_options);
        return context.NotificationStates.AsNoTracking().SingleOrDefault(s => s.Key == AccountNotificationRules.HitAndRunStateKey)?.Value;
    }

    private static List<Notification> Sent(Notifier notifier)
    {
        var sent = new List<Notification>();
        while (notifier.Reader.TryRead(out var notification))
        {
            sent.Add(notification);
        }

        return sent;
    }

    [Fact]
    public async Task Stored0Read1_IsSentAndTheCountStored()
    {
        Store(0);
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await watcher.CheckAsync(Account(1));

        var message = Assert.Single(Sent(notifier));
        Assert.Contains("MAM counts 1 hit-and-run", message.Message);
        Assert.Equal("1", Stored());
    }

    [Fact]
    public async Task Stored1Read1_SendsAndStoresNothing()
    {
        Store(1);
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await watcher.CheckAsync(Account(1));

        Assert.Empty(Sent(notifier));
        Assert.Equal("1", Stored());
    }

    [Fact]
    public async Task Stored2Read1_SendsNothingStoresTheLowerCountAndALaterRiseIsSent()
    {
        Store(2);
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await watcher.CheckAsync(Account(1));
        Assert.Empty(Sent(notifier));
        Assert.Equal("1", Stored());

        await watcher.CheckAsync(Account(2));

        Assert.Contains("2 hit-and-runs", Assert.Single(Sent(notifier)).Message);
        Assert.Equal("2", Stored());
    }

    [Fact]
    public async Task NothingStoredRead1_IsSentOnce_AndARestartWithTheStoredCountSendsNothing()
    {
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await watcher.CheckAsync(Account(1));
        await watcher.CheckAsync(Account(1));
        Assert.Single(Sent(notifier));

        var (afterRestart, restartedNotifier) = CreateWatcher(NotificationEvent.HitAndRun); // a new process, the same database
        await afterRestart.CheckAsync(Account(1));

        Assert.Empty(Sent(restartedNotifier));
        Assert.Equal("1", Stored());
    }

    [Fact]
    public async Task NothingStoredRead0_SendsAndStoresNothing()
    {
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await watcher.CheckAsync(Account(0));

        Assert.Empty(Sent(notifier));
        Assert.Null(Stored());
    }

    [Fact]
    public async Task NoBlockWantingHitAndRun_ComparesAndStoresNothing()
    {
        var (watcher, notifier) = CreateWatcher(NotificationEvent.Grabbed);

        await watcher.CheckAsync(Account(3));

        Assert.Empty(Sent(notifier));
        Assert.Null(Stored());
    }

    [Fact]
    public async Task AnAccountReadWithoutTheHitAndRunEntry_IsLeftAlone()
    {
        Store(2);
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await watcher.CheckAsync(Account(hitAndRuns: null));

        Assert.Empty(Sent(notifier));
        Assert.Equal("2", Stored());
    }

    [Fact]
    public async Task TwoReadsAtOnce_FindTheSameRiseOnce()
    {
        var (watcher, notifier) = CreateWatcher(NotificationEvent.HitAndRun);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => watcher.CheckAsync(Account(1))));

        Assert.Single(Sent(notifier));
    }

    [Fact]
    public async Task ADatabaseThatCantBeRead_IsLoggedNotThrown()
    {
        var notifier = new Notifier([Wanting(NotificationEvent.HitAndRun)]);
        var broken = new HitAndRunWatcher(new BrokenFactory(), notifier, new FixedTime(Now));

        await broken.CheckAsync(Account(1)); // must not throw: it runs next to a job

        Assert.Empty(Sent(notifier));
    }

    private sealed class BrokenFactory : IDbContextFactory<FetchshelfDbContext>
    {
        public FetchshelfDbContext CreateDbContext() => throw new InvalidOperationException("no database");
    }
}
