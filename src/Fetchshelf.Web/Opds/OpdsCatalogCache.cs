using Fetchshelf.Core.Opds;
using Fetchshelf.Data;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Opds;

namespace Fetchshelf.Web.Opds;

/// <summary>
/// The OPDS catalog, read from the database at most once a minute. A reader paging through a large library then
/// doesn't have every page re-read 10,000 torrents; a book the Link job adds shows up within a minute.
/// </summary>
public sealed class OpdsCatalogCache(IServiceScopeFactory scopeFactory, FetchshelfSettings settings, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private (OpdsCatalog Catalog, DateTimeOffset ReadAt)? _current;

    public async Task<OpdsCatalog> GetAsync(CancellationToken cancellationToken = default)
    {
        if (Fresh() is { } catalog)
        {
            return catalog;
        }

        // One read at a time: requests arriving meanwhile wait for it instead of each reading the database.
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (Fresh() is { } readMeanwhile)
            {
                return readMeanwhile;
            }

            using var scope = scopeFactory.CreateScope();
            var reader = new OpdsCatalogReader(
                scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>(), settings.Library, settings.Opds ?? new OpdsSettings());
            var read = await reader.ReadAsync(cancellationToken);
            _current = (read, _time.GetUtcNow());
            return read;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private OpdsCatalog? Fresh() =>
        _current is { } current && _time.GetUtcNow() - current.ReadAt < MaxAge ? current.Catalog : null;
}
