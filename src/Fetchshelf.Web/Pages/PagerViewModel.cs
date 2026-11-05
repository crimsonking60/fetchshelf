using System.Globalization;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The "Showing x of y. Page n of m." line, the page-size dropdown and the First/Previous/Next/Last links under a paged
/// list (<c>Shared/_Pager.cshtml</c>). <paramref name="RouteValues"/> holds the list's search, filters, sort and page
/// size, so every page link keeps them.
/// </summary>
public sealed record PagerViewModel(
    string Page, IReadOnlyDictionary<string, string> RouteValues, int PageNumber, int TotalPages, int ShownCount, int TotalCount,
    int PageSize, string Id, string Label)
{
    /// <summary>The route values for a link to the given page; a fresh dictionary each time, since a link's tag helper adds to it.</summary>
    public Dictionary<string, string> RouteValuesFor(int pageNumber) =>
        new(RouteValues) { ["PageNumber"] = pageNumber.ToString(CultureInfo.InvariantCulture) };

    /// <summary>The route values for the first page at another page size.</summary>
    public Dictionary<string, string> RouteValuesForPageSize(int pageSize) =>
        new(RouteValues) { ["PageSize"] = pageSize.ToString(CultureInfo.InvariantCulture) };

    /// <summary>The dropdown is left out while the whole list fits on one page at the default size.</summary>
    public bool ShowsPageSizes => TotalCount > Paging.DefaultPageSize || PageSize != Paging.DefaultPageSize;
}
