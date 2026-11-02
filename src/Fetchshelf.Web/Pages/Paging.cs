namespace Fetchshelf.Web.Pages;

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
