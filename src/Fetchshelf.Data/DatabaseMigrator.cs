using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>Applies pending EF Core migrations at startup, so an existing database is upgraded in place.</summary>
public static class DatabaseMigrator
{
    /// <remarks>
    /// Also switches the database to write-ahead logging, so pages can still read while a long job writes. The
    /// setting is stored in the database file, so this only changes something the first time.
    /// </remarks>
    public static void Migrate(FetchshelfDbContext dbContext)
    {
        dbContext.Database.Migrate();
        dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
