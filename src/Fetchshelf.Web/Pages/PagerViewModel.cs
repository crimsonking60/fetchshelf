using System.Globalization;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The "Showing x of y. Page n of m." line and the First/Previous/Next/Last links under a paged list
/// (<c>Shared/_Pager.cshtml</c>). <paramref name="RouteValues"/> holds the list's search, filters, sort and page size,
/// so every page link keeps them.
/// </summary>
public sealed record PagerViewModel(
    string Page, IReadOnlyDictionary<string, string> RouteValues, int PageNumber, int TotalPages, int ShownCount, int TotalCount,
    string Id, string Label)
{
    /// <summary>The route values for a link to the given page; a fresh dictionary each time, since a link's tag helper adds to it.</summary>
    public Dictionary<string, string> RouteValuesFor(int pageNumber) =>
        new(RouteValues) { ["PageNumber"] = pageNumber.ToString(CultureInfo.InvariantCulture) };
}

/// <summary>Cuts one page out of a sorted list the way every paged list does it.</summary>
public static class Paging
{
    public const int DefaultPageSize = 25;

    /// <summary>
    /// The items on the requested page, with the page number clamped to the pages there are (so a page past the end
    /// shows the last one) and the page count. A page size of 0 or less falls back to <see cref="DefaultPageSize"/>.
    /// </summary>
    public static (List<T> Items, int PageNumber, int TotalPages) Slice<T>(IEnumerable<T> ordered, int totalCount, int pageNumber, int pageSize)
    {
        if (pageSize <= 0)
        {
            pageSize = DefaultPageSize;
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        return (ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), pageNumber, totalPages);
    }
}
