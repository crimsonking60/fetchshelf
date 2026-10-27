using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fetchshelf.Data;

/// <summary>Design-time factory so `dotnet ef migrations` can create a context without a running host.</summary>
public sealed class FetchshelfDbContextFactory : IDesignTimeDbContextFactory<FetchshelfDbContext>
{
    public FetchshelfDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FetchshelfDbContext>()
            .UseSqlite("Data Source=design-time.db");
        return new FetchshelfDbContext(optionsBuilder.Options);
    }
}
