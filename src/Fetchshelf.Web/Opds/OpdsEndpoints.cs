using System.Text;
using System.Xml.Linq;
using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Opds;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Opds;
using Fetchshelf.Web.Catalog;

namespace Fetchshelf.Web.Opds;

/// <summary>
/// The OPDS catalog under `/opds`, mapped only when the configuration has an `[opds]` section. With a `username` and
/// `password` in `[opds]`, readers must log in (<see cref="OpdsLogin"/>); without, like the rest of Fetchshelf, it is for a
/// trusted network, or behind a reverse proxy that asks for credentials.
/// </summary>
public static class OpdsEndpoints
{
    public const int PageSize = 50;

    public static void MapOpdsEndpoints(this IEndpointRouteBuilder endpoints, OpdsSettings settings)
    {
        var opds = endpoints.MapGroup("/opds");
        if (settings.RequiresLogin)
        {
            opds.AddEndpointFilter(new OpdsLogin(settings, endpoints.ServiceProvider.GetRequiredService<ILogger<OpdsLogin>>()));
        }

        MapViews(opds);
        MapViews(opds.MapGroup("/libraries/{index:int}"));

        opds.MapGet("/libraries", async (BookCatalogCache cache, TimeProvider time, CancellationToken ct) =>
        {
            var catalog = await cache.GetOpdsAsync(ct);
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

        opds.MapGet("/search", async (BookCatalogCache cache, TimeProvider time, string? q, int? page, CancellationToken ct) =>
            BookFeed($"urn:fetchshelf:opds:search:{q}", $"Search: {q}", $"/opds/search?q={Uri.EscapeDataString(q ?? string.Empty)}",
                (await cache.GetOpdsAsync(ct)).Search(q ?? string.Empty), page, time));

        opds.MapGet("/books/{id}/files/{index:int}", async (BookCatalogCache cache, ILoggerFactory loggers, string id, int index, CancellationToken ct) =>
            (await cache.GetOpdsAsync(ct)).Find(id) is { } book
                ? BookFiles.Serve(book, CatalogFileKind.Ebook, index, loggers.CreateLogger(typeof(OpdsEndpoints)))
                : Results.NotFound());

        opds.MapGet("/books/{id}/cover", async (BookCatalogCache cache, OpdsCoverCache covers, HttpResponse response, string id, CancellationToken ct) =>
        {
            if ((await cache.GetOpdsAsync(ct)).Find(id) is not { } book || await covers.GetAsync(book, ct) is not { } cover)
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
        views.MapGet("/", async (BookCatalogCache cache, TimeProvider time, HttpContext context, CancellationToken ct) =>
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

        views.MapGet("/recent", async (BookCatalogCache cache, TimeProvider time, HttpContext context, int? page, CancellationToken ct) =>
            await ScopeAsync(cache, context, ct) is var (catalog, scope)
                ? BookFeed($"{scope.Id}:recent", scope.Title("Recently added"), $"{scope.Href}/recent", catalog.Recent(), page, time)
                : Results.NotFound());

        views.MapGet("/authors", async (BookCatalogCache cache, TimeProvider time, HttpContext context, int? page, CancellationToken ct) =>
            await ScopeAsync(cache, context, ct) is var (catalog, scope)
                ? GroupFeed($"{scope.Id}:authors", scope.Title("Authors"), $"{scope.Href}/authors", catalog.Authors(), page, time,
                    author => ($"{scope.Id}:author:{author}", $"{scope.Href}/author?name={Uri.EscapeDataString(author)}"))
                : Results.NotFound());

        views.MapGet("/author", async (BookCatalogCache cache, TimeProvider time, HttpContext context, string? name, int? page, CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(name) || await ScopeAsync(cache, context, ct) is not var (catalog, scope)
                || catalog.ByAuthor(name) is not { Count: > 0 } books)
            {
                return Results.NotFound();
            }

            return BookFeed($"{scope.Id}:author:{name}", name, $"{scope.Href}/author?name={Uri.EscapeDataString(name)}", books, page, time);
        });

        // Series with one book are left out unless asked for (singles=show), as on the Books page: hundreds of them would
        // bury the rest. The first page then starts with the way to them.
        views.MapGet("/series", async (BookCatalogCache cache, TimeProvider time, HttpContext context, int? page, string? singles, CancellationToken ct) =>
        {
            if (await ScopeAsync(cache, context, ct) is not var (catalog, scope))
            {
                return Results.NotFound();
            }

            var all = catalog.Series();
            var withSingles = singles == "show";
            var shown = withSingles ? all : all.Where(group => group.BookCount > 1).ToList();
            var hidden = all.Count - shown.Count;
            OpdsNavigationEntry? toSingles = hidden > 0
                ? new($"{scope.Id}:series:singles", "Every series, with those of one book", $"{Count(hidden, "series")} with one book left out of this list", $"{scope.Href}/series?singles=show", LeadsToBooks: false)
                : null;
            return GroupFeed(
                withSingles ? $"{scope.Id}:series:all" : $"{scope.Id}:series", scope.Title("Series"),
                withSingles ? $"{scope.Href}/series?singles=show" : $"{scope.Href}/series", shown, page, time,
                series => ($"{scope.Id}:series:{series}", $"{scope.Href}/series/books?name={Uri.EscapeDataString(series)}"),
                toSingles);
        });

        views.MapGet("/series/books", async (BookCatalogCache cache, TimeProvider time, HttpContext context, string? name, int? page, CancellationToken ct) =>
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
    private static async Task<(BookCatalog Catalog, CatalogScope Scope)?> ScopeAsync(BookCatalogCache cache, HttpContext context, CancellationToken ct)
    {
        var catalog = await cache.GetOpdsAsync(ct);
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

    private static IResult BookFeed(string id, string title, string href, IReadOnlyList<CatalogBook> books, int? page, TimeProvider time)
    {
        var (items, opdsPage) = OpdsPage.Of(books, page ?? 1, PageSize, number => WithPage(href, number));
        var feed = OpdsFeedBuilder.Acquisition(id, title, opdsPage.Number == 1 ? href : WithPage(href, opdsPage.Number), time.GetUtcNow(), items, opdsPage);
        return Results.Text(OpdsFeedBuilder.Serialize(feed), OpdsFeedBuilder.AcquisitionType, Encoding.UTF8);
    }

    private static IResult GroupFeed(
        string id, string title, string href, IReadOnlyList<CatalogGroup> groups, int? page, TimeProvider time, Func<string, (string Id, string Href)> linkFor,
        OpdsNavigationEntry? first = null)
    {
        var (items, opdsPage) = OpdsPage.Of(groups, page ?? 1, PageSize, number => WithPage(href, number));
        var entries = items.Select(group =>
        {
            var (entryId, entryHref) = linkFor(group.Name);
            return new OpdsNavigationEntry(entryId, group.Name, Count(group.BookCount, "book"), entryHref, LeadsToBooks: true);
        });
        if (first is not null && opdsPage.Number == 1)
        {
            entries = entries.Prepend(first);
        }

        return Navigation(OpdsFeedBuilder.Navigation(id, title, opdsPage.Number == 1 ? href : WithPage(href, opdsPage.Number), time.GetUtcNow(), entries, opdsPage));
    }

    private static string WithPage(string href, int page) => $"{href}{(href.Contains('?') ? '&' : '?')}page={page}";

    private static string Count(int count, string noun) =>
        $"{count} {noun switch { "series" => "series", "library" when count != 1 => "libraries", _ when count != 1 => noun + "s", _ => noun }}";

    private static IResult Navigation(XDocument feed) => Results.Text(OpdsFeedBuilder.Serialize(feed), OpdsFeedBuilder.NavigationType, Encoding.UTF8);
}
