namespace Fetchshelf.Web.Pages;

/// <summary>Cuts one page out of a sorted list the way every paged list does it.</summary>
public static class Paging
{
    public const int DefaultPageSize = 25;

    /// <summary>The page size that shows the whole list on one page.</summary>
    public const int All = 0;

    /// <summary>The choices in the page-size dropdown under every paged list.</summary>
    public static readonly IReadOnlyList<(int Size, string Label)> PageSizeOptions =
        [(DefaultPageSize, "25"), (50, "50"), (100, "100"), (500, "500"), (All, "All")];

    /// <summary>
    /// The items on the requested page, with the page number clamped to the pages there are (so a page past the end
    /// shows the last one) and the page count. A page size of <see cref="All"/> puts everything on one page, and a
    /// negative one falls back to <see cref="DefaultPageSize"/>.
    /// </summary>
    public static (List<T> Items, int PageNumber, int TotalPages) Slice<T>(IEnumerable<T> ordered, int totalCount, int pageNumber, int pageSize)
    {
        var window = Window(totalCount, pageNumber, pageSize);
        return (ordered.Skip(window.Skip).Take(window.Take).ToList(), window.PageNumber, window.TotalPages);
    }

    /// <summary>
    /// The same page as <see cref="Slice{T}"/> picks, as the rows to skip and take, for a list paged in the database: count
    /// the matches, then fetch only <see cref="PageWindow.Take"/> rows after <see cref="PageWindow.Skip"/>.
    /// </summary>
    public static PageWindow Window(int totalCount, int pageNumber, int pageSize)
    {
        if (pageSize == All)
        {
            return new PageWindow(0, Math.Max(totalCount, 0), 1, 1);
        }

        if (pageSize < 0)
        {
            pageSize = DefaultPageSize;
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        return new PageWindow((pageNumber - 1) * pageSize, pageSize, pageNumber, totalPages);
    }
}

/// <summary>The rows of one page: <see cref="Skip"/> rows in, at most <see cref="Take"/>, with its number and the page count.</summary>
public sealed record PageWindow(int Skip, int Take, int PageNumber, int TotalPages);
