namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>
/// The Torrents list's search, filters and sort, so a link that changes one of them (a sort heading, a filter value)
/// keeps the others. Paging is left out: any such change starts again at page 1.
/// </summary>
public sealed record TorrentListQuery(string? Q, string? Language, string? MediaType, string Sort, string Dir, int PageSize)
{
    /// <summary>The query as route values for a link, leaving out empty values.</summary>
    public Dictionary<string, string> ToRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Q)] = Q ?? string.Empty,
            [nameof(Language)] = Language ?? string.Empty,
            [nameof(MediaType)] = MediaType ?? string.Empty,
            [nameof(Sort)] = Sort,
            [nameof(Dir)] = Dir,
            [nameof(PageSize)] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Sorted by title: ascending first, reversed when already sorted by title ascending.</summary>
    public TorrentListQuery ToggleTitleSort() =>
        this with { Sort = "Title", Dir = Sort == "Title" && Dir == "asc" ? "desc" : "asc" };
}
