using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>Applies pending EF Core migrations at startup, Fetchshelf-spec.md §11 ("MUST open and be upgraded in place").</summary>
public static class DatabaseMigrator
{
    public static void Migrate(FetchshelfDbContext dbContext) => dbContext.Database.Migrate();
}
