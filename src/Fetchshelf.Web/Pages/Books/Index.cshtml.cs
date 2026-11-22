using System.Globalization;
using Fetchshelf.Core.Catalog;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fetchshelf.Web.Pages.Books;

/// <summary>
/// The Books page: every linked book, audiobooks included, to find one and open it in the app that plays or shows it
/// best. Browses by recently added, author, series or library, or searches; filters by format.
/// </summary>
public class IndexModel(BookCatalogCache catalogCache, FetchshelfSettings settings) : PageModel
{
    public const string ViewAuthors = "authors";
    public const string ViewSeries = "series";
    public const string ViewLibraries = "libraries";

    public static IReadOnlyList<(string Value, string Label)> Views { get; } =
        [("", "Recently added"), (ViewAuthors, "Authors"), (ViewSeries, "Series"), (ViewLibraries, "Libraries")];

    public static IReadOnlyList<(string Value, string Label)> Formats { get; } =
        [("", "All"), ("audiobooks", "Audiobooks"), ("ebooks", "Ebooks")];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Empty for recently added, or <see cref="ViewAuthors"/>, <see cref="ViewSeries"/>, <see cref="ViewLibraries"/>.</summary>
    [BindProperty(SupportsGet = true)]
    public string? View { get; set; }

    /// <summary>An author whose books are shown.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Author { get; set; }

    /// <summary>A series whose books are shown.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Series { get; set; }

    /// <summary>A library whose books are shown, by its index in the configuration.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Library { get; set; }

    /// <summary>Empty for all, "audiobooks" or "ebooks".</summary>
    [BindProperty(SupportsGet = true)]
    public string? Format { get; set; }

    /// <summary>A genre by MAM's id, or <see cref="GenreFilters.NoGenre"/>. Used only with `genres = true`.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Genre { get; set; }

    /// <summary><c>Fiction</c>, <c>Nonfiction</c> or <see cref="GenreFilters.Neither"/>. Used only with `genres = true`.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Fiction { get; set; }

    /// <summary>Whether `genres = true`, which shows the Genre and Fiction filters.</summary>
    public bool GenresOn => settings.Genres;

    /// <summary>The Genre filter's choices: the genres among the library's books, by MAM's names, and No genre.</summary>
    public IReadOnlyList<(string Value, string Label)> GenreOptions { get; private set; } = [];

    /// <summary>The Fiction filter's choices: Neither only when a book has neither main category.</summary>
    public IReadOnlyList<(string Value, string Label)> FictionOptions { get; private set; } = GenreFilters.FictionOptions;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = Paging.DefaultPageSize;

    /// <summary>What the page shows below the toolbar: a heading for a narrowed list, else null.</summary>
    public string? Heading { get; private set; }

    /// <summary>The books on this page, when it shows books.</summary>
    public IReadOnlyList<BookCardViewModel>? Books { get; private set; }

    /// <summary>The authors, series or libraries on this page, when it shows one of those lists.</summary>
    public IReadOnlyList<BookGroupLink>? Groups { get; private set; }

    /// <summary>The series shown, for the link to its row on the Series page.</summary>
    public string? SeriesShown { get; private set; }

    /// <summary>Whether the library has any book at all, before the search and filter.</summary>
    public bool LibraryEmpty { get; private set; }

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public int ShownCount => Books?.Count ?? Groups?.Count ?? 0;

    public CatalogFormat CatalogFormat => Format switch
    {
        "audiobooks" => CatalogFormat.Audiobooks,
        "ebooks" => CatalogFormat.Ebooks,
        _ => CatalogFormat.All,
    };

    /// <summary>The address's values without the page number: what every link on the page keeps.</summary>
    public Dictionary<string, string> RouteValues =>
        new Dictionary<string, string>
        {
            [nameof(Q)] = Q ?? string.Empty,
            [nameof(View)] = View ?? string.Empty,
            [nameof(Author)] = Author ?? string.Empty,
            [nameof(Series)] = Series ?? string.Empty,
            [nameof(Library)] = Library?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            [nameof(Format)] = Format ?? string.Empty,
            [nameof(Genre)] = GenresOn ? Genre ?? string.Empty : string.Empty,
            [nameof(Fiction)] = GenresOn ? Fiction ?? string.Empty : string.Empty,
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
        }
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>Whether the format, fiction or genre filter is in use.</summary>
    public bool HasFilters =>
        !string.IsNullOrEmpty(Format) || (GenresOn && (!string.IsNullOrEmpty(Genre) || !string.IsNullOrEmpty(Fiction)));

    /// <summary>The address without the format, fiction and genre filters: what is shown and the search stay.</summary>
    public Dictionary<string, string> WithoutFilters =>
        RouteValues.Where(pair => pair.Key is not (nameof(Format) or nameof(Genre) or nameof(Fiction))).ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>What is shown (a view, an author, a series or a library), for the filter form to keep when a filter changes.</summary>
    public IEnumerable<(string Name, string Value)> ShownRouteValues =>
        RouteValues.Where(pair => pair.Key is nameof(View) or nameof(Author) or nameof(Series) or nameof(Library)).Select(pair => (pair.Key, pair.Value));

    /// <summary>The route values for another view or list, keeping the format, genre and fiction filters and the page size.</summary>
    public Dictionary<string, string> RouteFor(string key, string value)
    {
        var route = new Dictionary<string, string> { [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrEmpty(Format))
        {
            route[nameof(Format)] = Format;
        }

        if (GenresOn && !string.IsNullOrEmpty(Genre))
        {
            route[nameof(Genre)] = Genre;
        }

        if (GenresOn && !string.IsNullOrEmpty(Fiction))
        {
            route[nameof(Fiction)] = Fiction;
        }

        if (value.Length > 0)
        {
            route[key] = value;
        }

        return route;
    }

    public PagerViewModel Pager => new("/Books/Index", RouteValues, PageNumber, TotalPages, ShownCount, TotalCount, PageSize, "books-pager", "Book pages");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PageSize = PageSizeCookie.Apply(HttpContext, "books", PageSize);
        var all = await catalogCache.GetAsync(cancellationToken);
        LibraryEmpty = all.Books.Count == 0;
        var format = CatalogFormat;
        var catalog = format == CatalogFormat.All ? all : all.Narrow(book => CatalogFiles.Matches(book, format));
        if (GenresOn)
        {
            GenreOptions = GenreFilters.Options(all.Books.SelectMany(book => book.GenreCategories).Distinct(), Genre);
            FictionOptions = GenreFilters.FictionChoices(all.Books.Any(book => book.FictionCategory is null), Fiction);
            if (!string.IsNullOrEmpty(Genre) || !string.IsNullOrEmpty(Fiction))
            {
                catalog = catalog.Narrow(book => GenreFilters.Matches(book.GenreCategories, book.FictionCategory, Genre, Fiction));
            }
        }

        var query = Q?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            Heading = $"Search: {query}";
            ShowBooks(catalog.Search(query));
        }
        else if (!string.IsNullOrEmpty(Author))
        {
            Heading = Author;
            ShowBooks(catalog.ByAuthor(Author));
        }
        else if (!string.IsNullOrEmpty(Series))
        {
            Heading = Series;
            SeriesShown = Series;
            ShowBooks(catalog.BySeries(Series), Series);
        }
        else if (Library is { } index)
        {
            Heading = all.Libraries.FirstOrDefault(library => library.Index == index)?.Name ?? "Library";
            ShowBooks(catalog.ByLibrary(index));
        }
        else if (View == ViewAuthors)
        {
            ShowGroups(catalog.Authors().Select(group => new BookGroupLink(group.Name, group.BookCount, RouteFor(nameof(Author), group.Name))).ToList());
        }
        else if (View == ViewSeries)
        {
            ShowGroups(catalog.Series().Select(group => new BookGroupLink(group.Name, group.BookCount, RouteFor(nameof(Series), group.Name))).ToList());
        }
        else if (View == ViewLibraries)
        {
            // Few enough to read down one column, A-Z.
            ShowGroups(catalog.Libraries
                .OrderBy(library => library.Name, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                .Select(library => new BookGroupLink(
                    library.Name, catalog.ByLibrary(library.Index).Count, RouteFor(nameof(Library), library.Index.ToString(CultureInfo.InvariantCulture))))
                .ToList());
        }
        else
        {
            ShowBooks(catalog.Recent());
        }
    }

    private void ShowBooks(IReadOnlyList<CatalogBook> books, string? series = null)
    {
        TotalCount = books.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(books, TotalCount, PageNumber, PageSize);
        Books = page.Select(book => BookCardViewModel.From(book, series)).ToList();
    }

    private void ShowGroups(IReadOnlyList<BookGroupLink> groups)
    {
        TotalCount = groups.Count;
        (var page, PageNumber, TotalPages) = Paging.Slice(groups, TotalCount, PageNumber, PageSize);
        Groups = page;
    }
}

/// <summary>An author, series or library in a list: its name, its number of books, and the route to them.</summary>
public sealed record BookGroupLink(string Name, int BookCount, Dictionary<string, string> Route);
