using Fetchshelf.Core.Catalog;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Opds;
using Fetchshelf.Web.Opds;

namespace Fetchshelf.Web.Catalog;

/// <summary>
/// The Books page's covers and downloads. Covers are always there; downloads only with an `[opds]` section, as the
/// page then serves what an OPDS reader could fetch anyway. With an `[opds]` login, both ask for it, like the catalog.
/// </summary>
public static class BookEndpoints
{
    public static string CoverHref(CatalogBook book) => $"/books/{Uri.EscapeDataString(book.Id)}/cover";

    public static string AudioZipHref(CatalogBook book) => $"/books/{Uri.EscapeDataString(book.Id)}/audio.zip";

    public static string FileHref(CatalogBook book, CatalogFileKind kind, int index) =>
        $"/books/{Uri.EscapeDataString(book.Id)}/{(kind == CatalogFileKind.Audio ? "audio" : "ebook")}/{index}";

    /// <summary>Whether the page offers downloads: only with an `[opds]` section.</summary>
    public static bool DownloadsAvailable(FetchshelfSettings settings) => settings.Opds is not null;

    public static void MapBookEndpoints(this IEndpointRouteBuilder endpoints, FetchshelfSettings settings)
    {
        var books = endpoints.MapGroup("/books/{id}");
        if (settings.Opds is { RequiresLogin: true } opds)
        {
            books.AddEndpointFilter(new OpdsLogin(opds, endpoints.ServiceProvider.GetRequiredService<ILogger<OpdsLogin>>()));
        }

        books.MapGet("/cover", async (BookCatalogCache cache, OpdsCoverCache covers, HttpResponse response, string id, CancellationToken ct) =>
        {
            if ((await cache.GetAsync(ct)).Find(id) is not { } book || await covers.GetAsync(book, ct) is not { } cover)
            {
                return Results.NotFound();
            }

            response.Headers.CacheControl = "private, max-age=86400";
            return Results.Bytes(cover.Bytes, cover.ContentType);
        });

        if (!DownloadsAvailable(settings))
        {
            return;
        }

        books.MapGet("/audio/{index:int}", (BookCatalogCache cache, ILoggerFactory loggers, string id, int index, CancellationToken ct) =>
            ServeAsync(cache, loggers, id, CatalogFileKind.Audio, index, ct));
        books.MapGet("/audio.zip", async (BookCatalogCache cache, ILoggerFactory loggers, string id, CancellationToken ct) =>
            (await cache.GetAsync(ct)).Find(id) is { } book
                ? BookFiles.ServeAudioZip(book, loggers.CreateLogger(typeof(BookEndpoints)))
                : Results.NotFound());
        books.MapGet("/ebook/{index:int}", (BookCatalogCache cache, ILoggerFactory loggers, string id, int index, CancellationToken ct) =>
            ServeAsync(cache, loggers, id, CatalogFileKind.Ebook, index, ct));
    }

    private static async Task<IResult> ServeAsync(
        BookCatalogCache cache, ILoggerFactory loggers, string id, CatalogFileKind kind, int index, CancellationToken ct) =>
        (await cache.GetAsync(ct)).Find(id) is { } book
            ? BookFiles.Serve(book, kind, index, loggers.CreateLogger(typeof(BookEndpoints)))
            : Results.NotFound();
}
