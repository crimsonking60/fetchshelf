using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Opds;
using Fetchshelf.Data;
using Fetchshelf.Data.Catalog;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Catalog;

/// <summary>
/// The <see cref="BookCatalog"/>, read from the database at most once a minute and shared by the OPDS feeds and the Books
/// page. A reader paging through a large library then doesn't have every page re-read 10,000 torrents; a book the Link
/// job adds shows up within a minute. Works with or without an `[opds]` section.
/// </summary>
public sealed class BookCatalogCache(IServiceScopeFactory scopeFactory, FetchshelfSettings settings, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Snapshot? _current;

    /// <summary>Every linked book.</summary>
    public async Task<BookCatalog> GetAsync(CancellationToken cancellationToken = default) =>
        (await SnapshotAsync(cancellationToken)).Catalog;

    /// <summary>What the OPDS catalog serves: the ebooks in the `[opds]` libraries.</summary>
    public async Task<BookCatalog> GetOpdsAsync(CancellationToken cancellationToken = default) =>
        (await SnapshotAsync(cancellationToken)).Opds.Value;

    private async Task<Snapshot> SnapshotAsync(CancellationToken cancellationToken)
    {
        if (Fresh() is { } snapshot)
        {
            return snapshot;
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
            var reader = new BookCatalogReader(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>(), settings.Library, settings.Genres);
            var catalog = await reader.ReadAsync(cancellationToken);
            var served = (settings.Opds ?? new OpdsSettings()).ServedLibraryIndexes(settings.Library);
            var read = new Snapshot(catalog, new Lazy<BookCatalog>(() => OpdsCatalog.From(catalog, served)), _time.GetUtcNow());
            _current = read;
            return read;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private Snapshot? Fresh() => _current is { } current && _time.GetUtcNow() - current.ReadAt < MaxAge ? current : null;

    private sealed record Snapshot(BookCatalog Catalog, Lazy<BookCatalog> Opds, DateTimeOffset ReadAt);
}
