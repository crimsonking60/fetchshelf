using System.Data.Common;
using System.Globalization;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>
/// The Fetchshelf version that last started on a database, kept in the database's <c>DatabaseInfo</c> table. A backup
/// is named after it, so the name says which version to run on the backup; and a version older than it learns, on
/// startup, that the database was made for a newer one.
/// </summary>
public static class FetchshelfVersionRecord
{
    public const string Key = "fetchshelf_version";

    /// <summary>The version on record, or null for a database from before the record existed, or a new one.</summary>
    public static string? Read(DbConnection connection)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DatabaseInfo';";
        if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            return null;
        }

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT Value FROM DatabaseInfo WHERE Key = $key;";
        var key = read.CreateParameter();
        key.ParameterName = "$key";
        key.Value = Key;
        read.Parameters.Add(key);
        return read.ExecuteScalar() as string;
    }

    /// <summary>The version on record in the database <paramref name="dbContext"/> is connected to.</summary>
    public static string? Read(FetchshelfDbContext dbContext)
    {
        dbContext.Database.OpenConnection();
        try
        {
            return Read(dbContext.Database.GetDbConnection());
        }
        finally
        {
            dbContext.Database.CloseConnection();
        }
    }

    /// <summary>
    /// Records <paramref name="current"/> as the version that last started on the migrated database, unless the database
    /// is on record as used by a newer release. Then that version stays on record, since the database still needs it
    /// and backups are named after it, and is returned so startup can say so. Null otherwise.
    /// </summary>
    public static string? RecordStartup(FetchshelfDbContext dbContext, string current, DateTimeOffset now)
    {
        var recorded = Read(dbContext);
        if (IsOlderRelease(current, recorded))
        {
            return recorded;
        }

        Write(dbContext, current, now);
        return null;
    }

    private static void Write(FetchshelfDbContext dbContext, string version, DateTimeOffset now)
    {
        var record = dbContext.DatabaseInfo.Find(Key);
        if (record is null)
        {
            dbContext.DatabaseInfo.Add(new DatabaseInfoEntity { Key = Key, Value = version, UpdatedAt = now });
        }
        else
        {
            record.Value = version;
            record.UpdatedAt = now;
        }

        dbContext.SaveChanges();
    }

    /// <summary>
    /// True when <paramref name="current"/> is an older release than <paramref name="recorded"/>, both <c>YYYY.MM.N</c>.
    /// Anything else, such as <c>dev</c> or <c>edge-&lt;commit&gt;</c>, can't be compared and is never older.
    /// </summary>
    private static bool IsOlderRelease(string current, string? recorded) =>
        recorded is not null && Release(current) is { } currentRelease && Release(recorded) is { } recordedRelease
        && currentRelease.CompareTo(recordedRelease) < 0;

    private static (int Year, int Month, int Number)? Release(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? (year, month, number)
                : null;
    }
}
