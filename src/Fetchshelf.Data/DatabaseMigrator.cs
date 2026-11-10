using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>Applies pending EF Core migrations at startup, so an existing database is upgraded in place.</summary>
public static class DatabaseMigrator
{
    /// <param name="dbContext">The context whose database is migrated.</param>
    /// <param name="writeAheadLog">
    /// Whether the database uses SQLite's write-ahead log (`database_wal`), so pages can still read while a long job
    /// writes. The journal mode is stored in the database file, so false switches a database back to SQLite's
    /// default mode rather than just not switching.
    /// </param>
    /// <param name="log">Receives one line per data upgrade that ran, for the startup log.</param>
    public static void Migrate(FetchshelfDbContext dbContext, bool writeAheadLog = false, Action<string>? log = null)
    {
        dbContext.Database.Migrate();
        dbContext.Database.ExecuteSqlRaw(writeAheadLog ? "PRAGMA journal_mode=WAL;" : "PRAGMA journal_mode=DELETE;");
        ApplyDataUpgrades(dbContext, log ?? (_ => { }));
    }

    /// <summary>
    /// The data version a database is at, kept in SQLite's <c>user_version</c>. EF Core tracks schema migrations in
    /// its own table and leaves <c>user_version</c> alone, so it numbers the changes to stored values that a schema
    /// migration can't make, because they need application code (such as recomputing title keys).
    /// </summary>
    public const int CurrentDataVersion = 1;

    private static void ApplyDataUpgrades(FetchshelfDbContext dbContext, Action<string> log)
    {
        var version = ReadDataVersion(dbContext);
        if (version >= CurrentDataVersion)
        {
            return;
        }

        using var transaction = dbContext.Database.BeginTransaction();
        if (version < 1)
        {
            // Version 1: title keys keep letters of every script, and fall back to the title itself.
            var (torrents, selections) = TitleKeys.RecomputeAll(dbContext);
            if (torrents + selections > 0)
            {
                log($"Recomputed the title keys of {torrents} torrents and {selections} selections for the new title rule");
            }
        }

        dbContext.Database.ExecuteSqlRaw($"PRAGMA user_version = {CurrentDataVersion};");
        transaction.Commit();
    }

    public static int ReadDataVersion(FetchshelfDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (opened)
            {
                connection.Close();
            }
        }
    }
}
