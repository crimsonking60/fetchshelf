using System.Globalization;
using Fetchshelf.Data.AutoDownloader;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// A list of finds' filters and sort (a follow's page, the To look at page), so a link that changes one of them keeps
/// the others. Paging is left out: any such change starts again at page 1.
/// </summary>
/// <param name="Show">Which finds, by what became of them.</param>
/// <param name="Format">"Audio" or "Ebook"; null for both.</param>
/// <param name="Sort">"Found", "Title", "Size", "Book" or "Follow".</param>
/// <param name="Dir">"asc" or "desc".</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record FindListQuery(FindShow Show, string? Format, string Sort, string Dir, int PageSize)
{
    public const string DefaultSort = "Found";

    /// <summary>The choices in the Show dropdown.</summary>
    public static readonly IReadOnlyList<(FindShow Value, string Label)> ShowOptions =
    [
        (FindShow.ToLookAt, "To look at"),
        (FindShow.NotFree, "Not free"),
        (FindShow.Selected, "Selected"),
        (FindShow.InLibrary, "In the library"),
        (FindShow.Dismissed, "Dismissed"),
        (FindShow.All, "All"),
    ];

    public static readonly IReadOnlyList<(string Value, string Label)> FormatOptions = [("Audio", "Audiobooks"), ("Ebook", "Ebooks")];

    public Dictionary<string, string> ToRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Show)] = Show == FindShow.ToLookAt ? string.Empty : Show.ToString(),
            [nameof(Format)] = Format ?? string.Empty,
            [nameof(Sort)] = Sort,
            [nameof(Dir)] = Dir,
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Whether a filter is set: a format, or something other than what is to look at.</summary>
    public bool HasFilters => Show != FindShow.ToLookAt || Format is not null;

    public FindListQuery WithoutFilters() => this with { Show = FindShow.ToLookAt, Format = null };

    /// <summary>Sorted by the column: in its natural order first, reversed when already sorted that way.</summary>
    public FindListQuery SortedBy(string column) =>
        this with { Sort = column, Dir = Sort == column ? (Dir == "asc" ? "desc" : "asc") : DefaultDir(column) };

    /// <summary>The newest found and the largest first; titles, books and names from the start.</summary>
    public static string DefaultDir(string column) => column is DefaultSort or "Size" ? "desc" : "asc";

    /// <summary>The rows in the query's order; the newest found first among equals.</summary>
    public IEnumerable<FindRow> Order(IEnumerable<FindRow> rows)
    {
        var ascending = Dir == "asc";
        IOrderedEnumerable<FindRow> ordered = Sort switch
        {
            "Title" => By(rows, r => r.Find.Title, StringComparer.OrdinalIgnoreCase, ascending),
            "Follow" => By(rows, r => r.Follow.DisplayName, StringComparer.OrdinalIgnoreCase, ascending),
            // Unknown sizes and unnumbered books last either way.
            "Size" => rows.OrderBy(r => r.Find.SizeBytes is null).ThenBy(r => r.Find.SizeBytes ?? 0, ascending),
            "Book" => rows.OrderBy(r => BookNumber(r.Find.SeriesDisplay) is null).ThenBy(r => BookNumber(r.Find.SeriesDisplay) ?? 0, ascending),
            _ => By(rows, r => r.Find.FoundAt, Comparer<DateTimeOffset>.Default, ascending),
        };
        return ordered.ThenByDescending(r => r.Find.FoundAt).ThenBy(r => r.Find.Id);
    }

    /// <summary>The number a series entry starts with, such as 5 for "5" or "5-7" and 2.5 for "2.5"; null for none.</summary>
    public static double? BookNumber(string? seriesDisplay)
    {
        var digits = new string((seriesDisplay ?? string.Empty).Trim().TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray()).TrimEnd('.');
        return double.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static IOrderedEnumerable<FindRow> By<TKey>(IEnumerable<FindRow> rows, Func<FindRow, TKey> key, IComparer<TKey> comparer, bool ascending) =>
        ascending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer);
}

internal static class FindRowOrdering
{
    public static IOrderedEnumerable<FindRow> ThenBy<TKey>(this IOrderedEnumerable<FindRow> rows, Func<FindRow, TKey> key, bool ascending) =>
        ascending ? rows.ThenBy(key) : rows.ThenByDescending(key);
}
