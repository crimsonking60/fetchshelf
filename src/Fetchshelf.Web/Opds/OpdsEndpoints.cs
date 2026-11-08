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

        MapViews(opds);
        MapViews(opds.MapGroup("/libraries/{index:int}"));

        opds.MapGet("/libraries", async (OpdsCatalogCache cache, TimeProvider time, CancellationToken ct) =>
        {
            var catalog = await cache.GetAsync(ct);
            var entries = catalog.Libraries.Select(library => new OpdsNavigationEntry(
                $"urn:fetchshelf:opds:library:{library.Index}", library.Name,
                Count(catalog.ByLibrary(library.Index).Count, "book"), $"/opds/libraries/{library.Index}", LeadsToBooks: false));
            return Navigation(OpdsFeedBuilder.Navigation("urn:fetchshelf:opds:libraries", "Libraries", "/opds/libraries", time.GetUtcNow(), entries));
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

    /// <summary>
    /// The views of a catalog: its start feed, recently added, authors and series. Mapped once for the whole catalog
    /// under `/opds`, and once for each library under `/opds/libraries/{index}`.
    /// </summary>
    private static void MapViews(RouteGroupBuilder views)
    {
        views.MapGet("/", async (OpdsCatalogCache cache, TimeProvider time, HttpContext context, CancellationToken ct) =>
        {
            if (await ScopeAsync(cache, context, ct) is not var (catalog, scope))
            {
                return Results.NotFound();
            }

            var entries = new List<OpdsNavigationEntry>
            {
                new($"{scope.Id}:recent", "Recently added", Count(catalog.Books.Count, "book"), $"{scope.Href}/recent", LeadsToBooks: true),
                new($"{scope.Id}:authors", "Authors", Count(catalog.Authors().Count, "author"), $"{scope.Href}/authors", LeadsToBooks: false),
                new($"{scope.Id}:series", "Series", Count(catalog.Series().Count, "series"), $"{scope.Href}/series", LeadsToBooks: false),
            };
            if (scope.Library is null && catalog.Libraries.Count > 1)
            {
                entries.Add(new("urn:fetchshelf:opds:libraries", "Libraries", Count(catalog.Libraries.Count, "library"), "/opds/libraries", LeadsToBooks: false));
            }

            var (id, title) = scope.Library is { } library ? (scope.Id, library) : ("urn:fetchshelf:opds:root", "Fetchshelf");
            return Navigation(OpdsFeedBuilder.Navigation(id, title, scope.Library is null ? OpdsFeedBuilder.RootHref : scope.Href, time.GetUtcNow(), entries));
        });

        views.MapGet("/recent", async (OpdsCatalogCache cache, TimeProvider time, HttpContext context, int? page, CancellationToken ct) =>
            await ScopeAsync(cache, context, ct) is var (catalog, scope)
                ? BookFeed($"{scope.Id}:recent", scope.Title("Recently added"), $"{scope.Href}/recent", catalog.Recent(), page, time)
                : Results.NotFound());

        views.MapGet("/authors", async (OpdsCatalogCache cache, TimeProvider time, HttpContext context, int? page, CancellationToken ct) =>
            await ScopeAsync(cache, context, ct) is var (catalog, scope)
                ? GroupFeed($"{scope.Id}:authors", scope.Title("Authors"), $"{scope.Href}/authors", catalog.Authors(), page, time,
                    author => ($"{scope.Id}:author:{author}", $"{scope.Href}/author?name={Uri.EscapeDataString(author)}"))
                : Results.NotFound());

        views.MapGet("/author", async (OpdsCatalogCache cache, TimeProvider time, HttpContext context, string? name, int? page, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(name) || await ScopeAsync(cache, context, ct) is not var (catalog, scope)
                || catalog.ByAuthor(name) is not { Count: > 0 } books)
            {
                return Results.NotFound();
            }

            return BookFeed($"{scope.Id}:author:{name}", name, $"{scope.Href}/author?name={Uri.EscapeDataString(name)}", books, page, time);
        });

        views.MapGet("/series", async (OpdsCatalogCache cache, TimeProvider time, HttpContext context, int? page, CancellationToken ct) =>
            await ScopeAsync(cache, context, ct) is var (catalog, scope)
                ? GroupFeed($"{scope.Id}:series", scope.Title("Series"), $"{scope.Href}/series", catalog.Series(), page, time,
                    series => ($"{scope.Id}:series:{series}", $"{scope.Href}/series/books?name={Uri.EscapeDataString(series)}"))
                : Results.NotFound());

        views.MapGet("/series/books", async (OpdsCatalogCache cache, TimeProvider time, HttpContext context, string? name, int? page, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(name) || await ScopeAsync(cache, context, ct) is not var (catalog, scope)
                || catalog.BySeries(name) is not { Count: > 0 } books)
            {
                return Results.NotFound();
            }

            return BookFeed($"{scope.Id}:series:{name}", name, $"{scope.Href}/series/books?name={Uri.EscapeDataString(name)}", books, page, time);
        });
    }

    /// <summary>Where a view's feeds live and what they are called: the whole catalog, or one library.</summary>
    private sealed record CatalogScope(string Href, string Id, string? Library)
    {
        public string Title(string view) => Library is null ? view : $"{view} in {Library}";
    }

    /// <summary>
    /// The catalog a request is for: the whole catalog, or the library its `{index}` names. Null for an index that
    /// isn't a served library with books.
    /// </summary>
    private static async Task<(OpdsCatalog Catalog, CatalogScope Scope)?> ScopeAsync(OpdsCatalogCache cache, HttpContext context, CancellationToken ct)
    {
        var catalog = await cache.GetAsync(ct);
        if (context.Request.RouteValues["index"] is not string raw)
        {
            return (catalog, new CatalogScope(OpdsFeedBuilder.RootHref, "urn:fetchshelf:opds", Library: null));
        }

        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            || catalog.ForLibrary(index) is not { } library)
        {
            return null;
        }

        return (library, new CatalogScope($"/opds/libraries/{index}", $"urn:fetchshelf:opds:library:{index}", library.Libraries[0].Name));
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
