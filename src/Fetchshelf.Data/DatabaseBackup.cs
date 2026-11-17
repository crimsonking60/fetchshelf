using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>A backup that was written: its full path and size.</summary>
public sealed record DatabaseBackupFile(string Path, long SizeBytes);

/// <summary>What one run of the Backup job did: the backup it wrote, and how many scheduled backups are kept now.</summary>
public sealed record DatabaseBackupRunSummary(DatabaseBackupFile Backup, int Kept);

/// <summary>
/// Copies the database with SQLite's online backup API, which takes a consistent copy while Fetchshelf reads and writes,
/// with or without the write-ahead log. Backups are named <c>data-&lt;UTC yyyyMMdd-HHmmss&gt;-&lt;version&gt;-&lt;reason&gt;.db</c>,
/// where the version is the one that last started on the database (<see cref="FetchshelfVersionRecord"/>), the one to
/// run on the backup. A database from before that record has no version in its backups' names. Pruning only ever
/// deletes files of these shapes.
/// </summary>
public static partial class DatabaseBackup
{
    /// <summary>The reason in the name of the Backup job's backups, which `keep` counts.</summary>
    public const string ScheduledReason = "scheduled";

    private const string BeforeUpgradePrefix = "before-";

    /// <summary>The reason in the name of a backup taken before upgrading to <paramref name="version"/>.</summary>
    public static string BeforeUpgradeReason(string version) =>
        BeforeUpgradePrefix + UnsafeNameCharacters().Replace(version, "_");

    /// <summary>
    /// Copies <paramref name="dataFile"/> into <paramref name="backupFolder"/>, creating the folder. The copy is written
    /// as <c>.db.partial</c>, switched to SQLite's default journal mode so it needs no `-wal` file, checked with
    /// <c>PRAGMA integrity_check</c>, and only then renamed to <c>.db</c>. A failure leaves no <c>.db</c> behind.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no database to back up.</exception>
    /// <exception cref="InvalidDataException">The copy failed the integrity check.</exception>
    public static DatabaseBackupFile Create(string dataFile, string backupFolder, string reason, DateTimeOffset now)
    {
        if (!File.Exists(dataFile))
        {
            throw new FileNotFoundException($"There is no database at {dataFile} to back up.", dataFile);
        }

        Directory.CreateDirectory(backupFolder);
        string? path = null;
        string? partial = null;
        try
        {
            using (var source = Open(dataFile, SqliteOpenMode.ReadWrite))
            {
                path = System.IO.Path.Combine(backupFolder, FileName(now, FetchshelfVersionRecord.Read(source), reason));
                partial = path + ".partial";
                DeleteCopy(partial);
                using var copy = Open(partial, SqliteOpenMode.ReadWriteCreate);
                source.BackupDatabase(copy);
                Execute(copy, "PRAGMA journal_mode=DELETE;");
                using var check = copy.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                var result = Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (!string.Equals(result, "ok", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"The backup of {dataFile} failed SQLite's integrity check: {result}");
                }
            }

            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            if (partial is not null)
            {
                DeleteCopy(partial);
            }

            throw;
        }

        return new DatabaseBackupFile(path, new FileInfo(path).Length);
    }

    /// <summary>
    /// <c>data-20261111-031500-2026.11.11-scheduled.db</c>, or without the version when the database has none on record.
    /// A <c>-</c> in the version, as in <c>edge-&lt;commit&gt;</c>, becomes <c>_</c>, so the name reads back unambiguously.
    /// </summary>
    public static string FileName(DateTimeOffset now, string? version, string reason)
    {
        var stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return version is null
            ? $"data-{stamp}-{reason}.db"
            : $"data-{stamp}-{UnsafeNameCharacters().Replace(version, "_").Replace('-', '_')}-{reason}.db";
    }

    /// <summary>One run of the Backup job: a scheduled backup, then the oldest scheduled ones beyond <paramref name="keep"/> deleted.</summary>
    public static DatabaseBackupRunSummary RunScheduled(string dataFile, string backupFolder, int keep, DateTimeOffset now)
    {
        var backup = Create(dataFile, backupFolder, ScheduledReason, now);
        Prune(backupFolder, reason => reason == ScheduledReason, keep);
        return new DatabaseBackupRunSummary(backup, Backups(backupFolder, reason => reason == ScheduledReason).Count);
    }

    /// <summary>
    /// Whether starting this version will change the database: it already has tables, and a schema migration or a data
    /// upgrade is waiting. A new database has nothing worth keeping, so it needs no backup.
    /// </summary>
    public static bool UpgradeIsPending(FetchshelfDbContext dbContext) =>
        dbContext.Database.GetAppliedMigrations().Any()
        && (dbContext.Database.GetPendingMigrations().Any() || DatabaseMigrator.ReadDataVersion(dbContext) < DatabaseMigrator.CurrentDataVersion);

    /// <summary>
    /// Takes the backup before an upgrade, when one is pending (<see cref="UpgradeIsPending"/>), and keeps the last
    /// <paramref name="keep"/> such backups. Returns the backup, or null when there was nothing to upgrade.
    /// </summary>
    public static DatabaseBackupFile? BeforeUpgrade(
        FetchshelfDbContext dbContext, string dataFile, string backupFolder, string version, int keep, DateTimeOffset now)
    {
        if (!UpgradeIsPending(dbContext))
        {
            return null;
        }

        var backup = Create(dataFile, backupFolder, BeforeUpgradeReason(version), now);
        Prune(backupFolder, reason => reason.StartsWith(BeforeUpgradePrefix, StringComparison.Ordinal), keep);
        return backup;
    }

    /// <summary>
    /// Deletes the oldest backups whose reason <paramref name="matches"/>, beyond the newest <paramref name="keep"/>.
    /// Only files named like a backup are looked at; nothing else in the folder is touched. Returns the deleted names.
    /// </summary>
    public static IReadOnlyList<string> Prune(string backupFolder, Func<string, bool> matches, int keep)
    {
        var deleted = new List<string>();
        foreach (var path in Backups(backupFolder, matches).Skip(keep))
        {
            File.Delete(path);
            deleted.Add(System.IO.Path.GetFileName(path));
        }

        return deleted;
    }

    /// <summary>The backups in <paramref name="backupFolder"/> whose reason <paramref name="matches"/>, newest first.</summary>
    private static List<string> Backups(string backupFolder, Func<string, bool> matches) =>
        !Directory.Exists(backupFolder)
            ? []
            : Directory.EnumerateFiles(backupFolder, "data-*.db")
                .Select(path => (Path: path, Match: BackupName().Match(System.IO.Path.GetFileName(path))))
                .Where(file => file.Match.Success && matches(file.Match.Groups["reason"].Value))
                // The UTC time stamp leads the name, so name order is time order.
                .OrderByDescending(file => System.IO.Path.GetFileName(file.Path), StringComparer.Ordinal)
                .Select(file => file.Path)
                .ToList();

    /// <summary>
    /// A connection of its own, outside the pool, so the files are let go of as soon as it is closed, with the busy
    /// timeout every Fetchshelf connection has.
    /// </summary>
    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        Execute(connection, $"PRAGMA busy_timeout = {(int)SqliteBusyTimeout.Timeout.TotalMilliseconds};");
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DeleteCopy(string partial)
    {
        File.Delete(partial);
        File.Delete(partial + "-journal");
    }

    /// <summary>A backup's name, with the version or, from before the version record, without it.</summary>
    [GeneratedRegex(@"^data-\d{8}-\d{6}-(?:(?<version>[A-Za-z0-9._]+)-)?(?<reason>scheduled|before-[A-Za-z0-9._-]+)\.db$")]
    private static partial Regex BackupName();

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeNameCharacters();
}
