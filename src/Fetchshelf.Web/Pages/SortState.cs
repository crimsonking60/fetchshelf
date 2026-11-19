namespace Fetchshelf.Web.Pages;

/// <summary>
/// How a list is sorted: by which column, in which direction. A click on a column heading asks for
/// <see cref="Toggle"/>: a column not yet sorted by starts in its natural direction, and the column sorted by flips.
/// A list always has an order, so there is no third click that removes it.
/// </summary>
public sealed record SortState(string Column, bool Descending)
{
    public const string Ascending = "asc";
    public const string DescendingValue = "desc";

    /// <summary>The state for a <c>Sort</c> and <c>Dir</c> pair as they come in a query string; anything but "desc" is ascending.</summary>
    public static SortState From(string column, string? direction) =>
        new(column, string.Equals(direction, DescendingValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>The direction as the query string carries it.</summary>
    public string Direction => Descending ? DescendingValue : Ascending;

    public bool IsSortedBy(string column) => string.Equals(Column, column, StringComparison.Ordinal);

    /// <summary>
    /// The state a click on <paramref name="column"/>'s heading leads to. <paramref name="firstDescending"/> is for
    /// numbers and dates, whose natural first direction is largest or newest first.
    /// </summary>
    public SortState Toggle(string column, bool firstDescending = false) =>
        IsSortedBy(column) ? this with { Descending = !Descending } : new SortState(column, firstDescending);

    /// <summary>The value of the <c>aria-sort</c> attribute for <paramref name="column"/>'s heading.</summary>
    public string AriaSort(string column) =>
        !IsSortedBy(column) ? "none" : Descending ? "descending" : "ascending";
}
