using Fetchshelf.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fetchshelf.Data.Tests;

/// <summary>Backups of the database with SQLite's online backup API, and pruning the old ones.</summary>
public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-backup-tests-").FullName;

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 3, 15, 0, TimeSpan.Zero);

    private string DataFile => Path.Combine(_tempDir, "data.db");

    private string BackupFolder => Path.Combine(_tempDir, "backups");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    private FetchshelfDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FetchshelfDbContext>().UseFetchshelfSqlite(DataFile).Options);

    private void SeedDatabase(bool writeAheadLog = false, int torrents = 3)
    {
        using var context = CreateContext();
        DatabaseMigrator.Migrate(context, writeAheadLog);
        for (var i = 1; i <= torrents; i++)
        {
            context.Torrents.Add(Torrent($"hash{i}", i));
        }

        context.SaveChanges();
    }

    private static TorrentEntity Torrent(string id, long mamId) => new()
    {
        Id = id,
        MamId = mamId,
        Title = $"Book {mamId}",
        NormalizedTitleKey = $"book {mamId}",
        MetadataJson = Fetchshelf.Core.Organizer.TorrentMetadataSerializer.Serialize(OrganizerTestSupport.BuildMetadata($"Book {mamId}")),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static FetchshelfDbContext OpenBackup(string path) =>
        new(new DbContextOptionsBuilder<FetchshelfDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

    private static string IntegrityCheck(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return (string)command.ExecuteScalar()!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_CopiesEveryRow_IntoAFileNamedByTimeAndReason(bool writeAheadLog)
    {
        SeedDatabase(writeAheadLog);

        var backup = DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now);

        Assert.Equal(Path.Combine(BackupFolder, "data-20261111-031500-scheduled.db"), backup.Path);
        Assert.Equal(new FileInfo(backup.Path).Length, backup.SizeBytes);
        using var copy = OpenBackup(backup.Path);
        Assert.Equal(3, copy.Torrents.Count());
        Assert.Equal("ok", IntegrityCheck(backup.Path));
    }

    [Fact]
    public void Create_FromADatabaseInWalMode_WritesACopyThatNeedsNoWalFile()
    {
        SeedDatabase(writeAheadLog: true);

        var backup = DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now);

        using var connection = new SqliteConnection($"Data Source={backup.Path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("delete", (string)command.ExecuteScalar()!);
        Assert.Equal(["data-20261111-031500-scheduled.db"], Directory.GetFiles(BackupFolder).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WhileAnotherConnectionWrites_GivesACopyThatPassesTheIntegrityCheck(bool writeAheadLog)
    {
        SeedDatabase(writeAheadLog, torrents: 0);
        using var stop = new CancellationTokenSource();
        var writes = Task.Run(() =>
        {
            using var context = CreateContext();
            var written = 0;
            while (!stop.IsCancellationRequested)
            {
                written++;
                context.Torrents.Add(Torrent($"hash{written}", written));
                context.SaveChanges();
            }

            return written;
        });

        var backups = new List<DatabaseBackupFile>();
        for (var i = 0; i < 5; i++)
        {
            backups.Add(DatabaseBackup.Create(DataFile, BackupFolder, $"test{i}", Now));
        }

        await stop.CancelAsync();
        var written = await writes;

        Assert.True(written > 0);
        Assert.All(backups, backup =>
        {
            Assert.Equal("ok", IntegrityCheck(backup.Path));
            using var copy = OpenBackup(backup.Path);
            Assert.InRange(copy.Torrents.Count(), 0, written);
        });
    }

    [Fact]
    public void Create_NoDatabase_ThrowsAndCreatesNone()
    {
        Assert.Throws<FileNotFoundException>(() => DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now));

        Assert.False(File.Exists(DataFile));
        Assert.False(Directory.Exists(BackupFolder) && Directory.EnumerateFiles(BackupFolder).Any());
    }

    [Fact]
    public void Create_ADatabaseThatCantBeRead_LeavesNoBackupBehind()
    {
        File.WriteAllText(DataFile, "this is not an SQLite database, but long enough to look like a file of one");

        Assert.ThrowsAny<SqliteException>(() => DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now));

        Assert.Empty(Directory.GetFiles(BackupFolder));
    }

    [Fact]
    public void Create_ALeftoverPartialCopy_IsReplaced()
    {
        SeedDatabase();
        Directory.CreateDirectory(BackupFolder);
        File.WriteAllText(Path.Combine(BackupFolder, "data-20261111-031500-scheduled.db.partial"), "left by an interrupted backup");

        var backup = DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now);

        Assert.Equal("ok", IntegrityCheck(backup.Path));
        Assert.Equal(["data-20261111-031500-scheduled.db"], Directory.GetFiles(BackupFolder).Select(Path.GetFileName));
    }

    [Fact]
    public void RunScheduled_KeepsTheNewest_AndTouchesNoOtherFile()
    {
        SeedDatabase();
        for (var day = 1; day <= 4; day++)
        {
            DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now.AddDays(-day));
        }

        DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.BeforeUpgradeReason("2026.11.9"), Now.AddDays(-10));
        File.WriteAllText(Path.Combine(BackupFolder, "notes.txt"), "mine");
        File.WriteAllText(Path.Combine(BackupFolder, "data-old-copy.db"), "mine too");

        var summary = DatabaseBackup.RunScheduled(DataFile, BackupFolder, keep: 2, Now);

        Assert.Equal(2, summary.Kept);
        Assert.Equal(Path.Combine(BackupFolder, "data-20261111-031500-scheduled.db"), summary.Backup.Path);
        Assert.Equal(
            ["data-20261101-031500-before-2026.11.9.db", "data-20261110-031500-scheduled.db", "data-20261111-031500-scheduled.db", "data-old-copy.db", "notes.txt"],
            Directory.GetFiles(BackupFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BeforeUpgradeReason_KeepsTheVersionReadable_AndSafeAsAFileName()
    {
        Assert.Equal("before-2026.11.9", DatabaseBackup.BeforeUpgradeReason("2026.11.9"));
        Assert.Equal("before-edge-abc123", DatabaseBackup.BeforeUpgradeReason("edge-abc123"));
        Assert.Equal("before-a_b_c", DatabaseBackup.BeforeUpgradeReason("a/b c"));
    }

    [Fact]
    public void BeforeUpgrade_NewDatabase_TakesNoBackup()
    {
        using var context = CreateContext();

        Assert.Null(DatabaseBackup.BeforeUpgrade(context, DataFile, BackupFolder, "2026.11.10", keep: 3, Now));
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void BeforeUpgrade_DatabaseUpToDate_TakesNoBackup()
    {
        SeedDatabase();
        using var context = CreateContext();

        Assert.Null(DatabaseBackup.BeforeUpgrade(context, DataFile, BackupFolder, "2026.11.10", keep: 3, Now));
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void BeforeUpgrade_PendingMigration_BacksUpTheDatabaseAsItWasBeforeMigrating()
    {
        SeedDatabase();
        string latest;
        using (var context = CreateContext())
        {
            // Back to the migration before the latest: the database an older version left.
            var migrations = context.Database.GetMigrations().ToList();
            latest = migrations[^1];
            context.GetService<IMigrator>().Migrate(migrations[^2]);
        }

        using (var context = CreateContext())
        {
            var backup = DatabaseBackup.BeforeUpgrade(context, DataFile, BackupFolder, "2026.11.10", keep: 3, Now);

            Assert.Equal(Path.Combine(BackupFolder, "data-20261111-031500-before-2026.11.10.db"), backup!.Path);
            DatabaseMigrator.Migrate(context);
        }

        using var copy = OpenBackup(Path.Combine(BackupFolder, "data-20261111-031500-before-2026.11.10.db"));
        Assert.Contains(latest, copy.Database.GetPendingMigrations());
        Assert.Equal(3, copy.Torrents.Count());
    }

    [Fact]
    public void BeforeUpgrade_DataUpgradePending_BacksUp_AndKeepsTheLastBeforeUpgradeBackups()
    {
        SeedDatabase();
        for (var day = 1; day <= 3; day++)
        {
            DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.BeforeUpgradeReason($"2026.11.{day}"), Now.AddDays(-day));
        }

        DatabaseBackup.Create(DataFile, BackupFolder, DatabaseBackup.ScheduledReason, Now.AddDays(-20));
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw("PRAGMA user_version = 1;");

        var backup = DatabaseBackup.BeforeUpgrade(context, DataFile, BackupFolder, "2026.11.10", keep: 3, Now);

        Assert.NotNull(backup);
        Assert.Equal(
            ["data-20261022-031500-scheduled.db", "data-20261109-031500-before-2026.11.2.db", "data-20261110-031500-before-2026.11.1.db",
                "data-20261111-031500-before-2026.11.10.db"],
            Directory.GetFiles(BackupFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
