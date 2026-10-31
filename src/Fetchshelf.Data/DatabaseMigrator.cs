using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>Applies pending EF Core migrations at startup, so an existing database is upgraded in place.</summary>
public static class DatabaseMigrator
{
    public static void Migrate(FetchshelfDbContext dbContext) => dbContext.Database.Migrate();
}
