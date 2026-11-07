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
    public static void Migrate(FetchshelfDbContext dbContext, bool writeAheadLog = false)
    {
        dbContext.Database.Migrate();
        dbContext.Database.ExecuteSqlRaw(writeAheadLog ? "PRAGMA journal_mode=WAL;" : "PRAGMA journal_mode=DELETE;");
    }
}
