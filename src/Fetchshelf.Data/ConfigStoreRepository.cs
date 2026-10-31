using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data;

/// <summary>Reads and writes the persistent key/value Config store.</summary>
public sealed class ConfigStoreRepository(FetchshelfDbContext dbContext)
{
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ConfigStore.FindAsync([key], cancellationToken);
        if (existing is null)
        {
            dbContext.ConfigStore.Add(new ConfigStoreEntry { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.ConfigStore.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        return entry?.Value;
    }
}
