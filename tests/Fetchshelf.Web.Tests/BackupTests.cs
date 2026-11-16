using Fetchshelf.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fetchshelf.Web.Tests;

/// <summary>The `[backup]` section as the running app uses it: the Backup job, and the backup taken before an upgrade.</summary>
public sealed class BackupTests
{
    private const string BackupConfig = """
        mam_id = "test-mam-id"

        [backup]
        folder = "backups"
        interval = 0
        """;

    private static string[] Backups(FetchshelfWebAppFixture fixture) =>
        Directory.Exists(Path.Combine(fixture.DataDirectory, "backups"))
            ? Directory.GetFiles(Path.Combine(fixture.DataDirectory, "backups")).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray()
            : [];

    [Fact]
    public async Task BackupJob_RunNow_WritesABackupIntoTheFolder()
    {
        using var fixture = new FetchshelfWebAppFixture(BackupConfig);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Jobs");
        await fixture.JobRegistry.TriggerAsync("Backup");

        Assert.Contains("Copies the database into the [backup] folder", html);
        var run = fixture.JobRegistry.Get("Backup")!.LastRun!;
        Assert.Matches(@"^Backed up .+ to data-\d{8}-\d{6}-scheduled\.db; 1 kept$", run.Summary);
        Assert.Matches(@"^data-\d{8}-\d{6}-scheduled\.db$", Assert.Single(Backups(fixture)));
    }

    [Fact]
    public async Task NoBackupSection_RegistersNoBackupJob()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"test-mam-id\"\n");
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/Jobs");

        Assert.Null(fixture.JobRegistry.Get("Backup"));
    }

    [Fact]
    public async Task Startup_NewDatabase_TakesNoBackup()
    {
        using var fixture = new FetchshelfWebAppFixture(BackupConfig);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/Jobs");

        Assert.Empty(Backups(fixture));
    }

    [Fact]
    public async Task Startup_DatabaseAnOlderVersionLeft_IsBackedUpBeforeItIsMigrated()
    {
        using var fixture = new FetchshelfWebAppFixture(BackupConfig);
        var dataFile = Path.Combine(fixture.DataDirectory, "data.db");
        using (var context = new FetchshelfDbContext(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={dataFile}").Options))
        {
            DatabaseMigrator.Migrate(context);
            var migrations = context.Database.GetMigrations().ToList();
            context.GetService<IMigrator>().Migrate(migrations[^2]);
        }

        SqliteConnection.ClearAllPools();

        using var client = fixture.CreateClient();
        await client.GetStringAsync("/Jobs");

        var backup = Assert.Single(Backups(fixture));
        Assert.Matches(@"^data-\d{8}-\d{6}-before-.+\.db$", backup);
        using var copy = new FetchshelfDbContext(new DbContextOptionsBuilder<FetchshelfDbContext>()
            .UseSqlite($"Data Source={Path.Combine(fixture.DataDirectory, "backups", backup)};Pooling=False").Options);
        Assert.Single(copy.Database.GetPendingMigrations());
    }

    [Fact]
    public async Task Startup_BeforeUpgradeOff_TakesNoBackup()
    {
        using var fixture = new FetchshelfWebAppFixture(BackupConfig + "\nbefore_upgrade = false\n");
        var dataFile = Path.Combine(fixture.DataDirectory, "data.db");
        using (var context = new FetchshelfDbContext(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={dataFile}").Options))
        {
            DatabaseMigrator.Migrate(context);
            context.Database.ExecuteSqlRaw("PRAGMA user_version = 1;");
        }

        SqliteConnection.ClearAllPools();

        using var client = fixture.CreateClient();
        await client.GetStringAsync("/Jobs");

        Assert.Empty(Backups(fixture));
    }

    [Fact]
    public async Task ConfigPage_ShowsTheBackupSettings()
    {
        using var fixture = new FetchshelfWebAppFixture(BackupConfig);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Config");

        Assert.Contains("id=\"backup-settings\"", html);
        Assert.Contains("<td>backups</td>", html);
        Assert.Contains("Jobs page only", html);
    }
}
