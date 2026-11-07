using System.Text;
using System.Xml.Linq;
using Fetchshelf.Core.Opds;
using Fetchshelf.Data.Opds;

namespace Fetchshelf.Web.Opds;

/// <summary>
/// The OPDS catalog under `/opds`, mapped only when the configuration has an `[opds]` section. No login, like the
/// rest of Fetchshelf: readers on a trusted network, or behind a reverse proxy that asks for credentials.
/// </summary>
public static class OpdsEndpoints
{
    public const int PageSize = 50;

    public static void MapOpdsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var opds = endpoints.MapGroup("/opds");

        opds.MapGet("/", async (OpdsCatalogCache cache, TimeProvider time, CancellationToken ct) =>
        {
            var catalog = await cache.GetAsync(ct);
            var entries = new List<OpdsNavigationEntry>
            {
                new("urn:fetchshelf:opds:recent", "Recently added", Count(catalog.Books.Count, "book"), "/opds/recent", LeadsToBooks: true),
                new("urn:fetchshelf:opds:authors", "Authors", Count(catalog.Authors().Count, "author"), "/opds/authors", LeadsToBooks: false),
                new("urn:fetchshelf:opds:series", "Series", Count(catalog.Series().Count, "series"), "/opds/series", LeadsToBooks: false),
            };
            if (catalog.Libraries.Count > 1)
            {
                entries.Add(new("urn:fetchshelf:opds:libraries", "Libraries", Count(catalog.Libraries.Count, "library"), "/opds/libraries", LeadsToBooks: false));
            }

            return Navigation(OpdsFeedBuilder.Navigation("urn:fetchshelf:opds:root", "Fetchshelf", OpdsFeedBuilder.RootHref, time.GetUtcNow(), entries));
        });

        opds.MapGet("/recent", async (OpdsCatalogCache cache, TimeProvider time, int? page, CancellationToken ct) =>
            BookFeed("urn:fetchshelf:opds:recent", "Recently added", "/opds/recent", (await cache.GetAsync(ct)).Recent(), page, time));

        opds.MapGet("/authors", async (OpdsCatalogCache cache, TimeProvider time, int? page, CancellationToken ct) =>
            GroupFeed("urn:fetchshelf:opds:authors", "Authors", "/opds/authors", (await cache.GetAsync(ct)).Authors(), page, time,
                author => ($"urn:fetchshelf:opds:author:{author}", $"/opds/author?name={Uri.EscapeDataString(author)}")));

        opds.MapGet("/author", async (OpdsCatalogCache cache, TimeProvider time, string? name, int? page, CancellationToken ct) =>
        {
            var books = string.IsNullOrEmpty(name) ? [] : (await cache.GetAsync(ct)).ByAuthor(name);
            return books.Count == 0
                ? Results.NotFound()
                : BookFeed($"urn:fetchshelf:opds:author:{name}", name!, $"/opds/author?name={Uri.EscapeDataString(name!)}", books, page, time);
        });

        opds.MapGet("/series", async (OpdsCatalogCache cache, TimeProvider time, int? page, CancellationToken ct) =>
            GroupFeed("urn:fetchshelf:opds:series", "Series", "/opds/series", (await cache.GetAsync(ct)).Series(), page, time,
                series => ($"urn:fetchshelf:opds:series:{series}", $"/opds/series/books?name={Uri.EscapeDataString(series)}")));

        opds.MapGet("/series/books", async (OpdsCatalogCache cache, TimeProvider time, string? name, int? page, CancellationToken ct) =>
        {
            var books = string.IsNullOrEmpty(name) ? [] : (await cache.GetAsync(ct)).BySeries(name);
            return books.Count == 0
                ? Results.NotFound()
                : BookFeed($"urn:fetchshelf:opds:series:{name}", name!, $"/opds/series/books?name={Uri.EscapeDataString(name!)}", books, page, time);
        });

        opds.MapGet("/libraries", async (OpdsCatalogCache cache, TimeProvider time, CancellationToken ct) =>
        {
            var catalog = await cache.GetAsync(ct);
            var entries = catalog.Libraries.Select(library => new OpdsNavigationEntry(
                $"urn:fetchshelf:opds:library:{library.Index}", library.Name,
                Count(catalog.ByLibrary(library.Index).Count, "book"), $"/opds/libraries/{library.Index}", LeadsToBooks: true));
            return Navigation(OpdsFeedBuilder.Navigation("urn:fetchshelf:opds:libraries", "Libraries", "/opds/libraries", time.GetUtcNow(), entries));
        });

        opds.MapGet("/libraries/{index:int}", async (OpdsCatalogCache cache, TimeProvider time, int index, int? page, CancellationToken ct) =>
        {
            var catalog = await cache.GetAsync(ct);
            return catalog.Libraries.FirstOrDefault(library => library.Index == index) is { } library
                ? BookFeed($"urn:fetchshelf:opds:library:{index}", library.Name, $"/opds/libraries/{index}", catalog.ByLibrary(index), page, time)
                : Results.NotFound();
        });

        // Readers fill in the template themselves, so it must be a full address; the request's own host is the one
        // the reader knows Fetchshelf by.
        opds.MapGet("/search.xml", (HttpRequest request) =>
            Results.Text(
                OpdsFeedBuilder.Serialize(OpdsFeedBuilder.OpenSearchDescription($"{request.Scheme}://{request.Host}/opds/search?q={{searchTerms}}")),
                OpdsFeedBuilder.OpenSearchType, Encoding.UTF8));

        opds.MapGet("/search", async (OpdsCatalogCache cache, TimeProvider time, string? q, int? page, CancellationToken ct) =>
            BookFeed($"urn:fetchshelf:opds:search:{q}", $"Search: {q}", $"/opds/search?q={Uri.EscapeDataString(q ?? string.Empty)}",
                (await cache.GetAsync(ct)).Search(q ?? string.Empty), page, time));

        opds.MapGet("/books/{id}/files/{index:int}", async (OpdsCatalogCache cache, ILoggerFactory loggers, string id, int index, CancellationToken ct) =>
        {
            if ((await cache.GetAsync(ct)).Find(id) is not { } book || index < 0 || index >= book.Files.Count)
            {
                return Results.NotFound();
            }

            // Nothing from the request becomes part of the path: the book and file come from the catalog, and the
            // stored relative path must still lead inside the book's folder.
            var folder = Path.GetFullPath(book.Folder);
            var path = Path.GetFullPath(Path.Combine(folder, book.Files[index].RelativePath));
            if (!path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            if (!File.Exists(path))
            {
                loggers.CreateLogger(typeof(OpdsEndpoints)).LogWarning("OPDS download of {Title}: {Path} is missing", book.Title, path);
                return Results.NotFound();
            }

            var file = book.Files[index];
            return Results.File(path, OpdsFeedBuilder.MediaTypeFor(file.Extension), DownloadName(book, index), enableRangeProcessing: true);
        });

        opds.MapGet("/books/{id}/cover", async (OpdsCatalogCache cache, OpdsCoverCache covers, HttpResponse response, string id, CancellationToken ct) =>
        {
            if ((await cache.GetAsync(ct)).Find(id) is not { } book || await covers.GetAsync(book, ct) is not { } cover)
            {
                return Results.NotFound();
            }

            response.Headers.CacheControl = "public, max-age=86400";
            return Results.Bytes(cover.Bytes, cover.ContentType);
        });
    }

    /// <summary>The name a downloaded file gets: "Title - Author.epub", with a part number when a book has several files.</summary>
    public static string DownloadName(OpdsBook book, int index)
    {
        var name = book.Authors.Count > 0 ? $"{book.Title} - {book.Authors[0]}" : book.Title;
        if (book.Files.Count > 1)
        {
            name += $" ({index + 1})";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        return $"{(safe.Length > 0 ? safe : "book")}.{book.Files[index].Extension}";
    }

    private static IResult BookFeed(string id, string title, string href, IReadOnlyList<OpdsBook> books, int? page, TimeProvider time)
    {
        var (items, opdsPage) = OpdsPage.Of(books, page ?? 1, PageSize, number => WithPage(href, number));
        var feed = OpdsFeedBuilder.Acquisition(id, title, opdsPage.Number == 1 ? href : WithPage(href, opdsPage.Number), time.GetUtcNow(), items, opdsPage);
        return Results.Text(OpdsFeedBuilder.Serialize(feed), OpdsFeedBuilder.AcquisitionType, Encoding.UTF8);
    }

    private static IResult GroupFeed(
        string id, string title, string href, IReadOnlyList<OpdsGroup> groups, int? page, TimeProvider time, Func<string, (string Id, string Href)> linkFor)
    {
        var (items, opdsPage) = OpdsPage.Of(groups, page ?? 1, PageSize, number => WithPage(href, number));
        var entries = items.Select(group =>
        {
            var (entryId, entryHref) = linkFor(group.Name);
            return new OpdsNavigationEntry(entryId, group.Name, Count(group.BookCount, "book"), entryHref, LeadsToBooks: true);
        });
        return Navigation(OpdsFeedBuilder.Navigation(id, title, opdsPage.Number == 1 ? href : WithPage(href, opdsPage.Number), time.GetUtcNow(), entries, opdsPage));
    }

    private static string WithPage(string href, int page) => $"{href}{(href.Contains('?') ? '&' : '?')}page={page}";

    private static string Count(int count, string noun) =>
        $"{count} {noun switch { "series" => "series", "library" when count != 1 => "libraries", _ when count != 1 => noun + "s", _ => noun }}";

    private static IResult Navigation(XDocument feed) => Results.Text(OpdsFeedBuilder.Serialize(feed), OpdsFeedBuilder.NavigationType, Encoding.UTF8);
}
