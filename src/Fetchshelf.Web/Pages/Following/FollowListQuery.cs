using System.Globalization;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// The Following page's search, filters and sort, so a link that changes one of them keeps the others. Paging is left
/// out: any such change starts again at page 1.
/// </summary>
/// <param name="Q">Words in the series' or author's name.</param>
/// <param name="Kind">Series or authors only.</param>
/// <param name="Mode">Those on Auto, or the followed ones, only.</param>
/// <param name="Waiting">Only those with finds to look at.</param>
/// <param name="Sort">"Name", "ToLookAt" or "Searched".</param>
/// <param name="Dir">"asc" or "desc".</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record FollowListQuery(string? Q, FollowKind? Kind, FollowMode? Mode, bool Waiting, string Sort, string Dir, int PageSize)
{
    public const string DefaultSort = "Name";

    public Dictionary<string, string> ToRouteValues()
    {
        var values = new Dictionary<string, string>
        {
            [nameof(Q)] = Q ?? string.Empty,
            [nameof(Kind)] = Kind?.ToString() ?? string.Empty,
            [nameof(Mode)] = Mode?.ToString() ?? string.Empty,
            [nameof(Waiting)] = Waiting ? "true" : string.Empty,
            [nameof(Sort)] = Sort,
            [nameof(Dir)] = Dir,
            [nameof(PageSize)] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        return values.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Whether any filter is set, not counting the search text.</summary>
    public bool HasFilters => Kind is not null || Mode is not null || Waiting;

    public FollowListQuery WithoutFilters() => this with { Kind = null, Mode = null, Waiting = false };

    /// <summary>Sorted by the column: in its natural order first, reversed when already sorted that way.</summary>
    public FollowListQuery SortedBy(string column) =>
        this with { Sort = column, Dir = Sort == column ? (Dir == "asc" ? "desc" : "asc") : DefaultDir(column) };

    /// <summary>Names A to Z; the most to look at, and the latest searched, first.</summary>
    public static string DefaultDir(string column) => column == DefaultSort ? "asc" : "desc";

    /// <summary>The follows the query matches, in its order.</summary>
    public IEnumerable<FollowSummary> Apply(IEnumerable<FollowSummary> summaries)
    {
        var words = (Q ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = summaries.Where(s =>
            (Kind is null || s.Follow.Kind == Kind)
            && (Mode is null || s.Follow.Mode == Mode)
            && (!Waiting || s.ToLookAt > 0)
            && words.All(word => s.Follow.DisplayName.Contains(word, StringComparison.OrdinalIgnoreCase)));
        var byName = (Func<FollowSummary, string>)(s => s.Follow.DisplayName);
        IOrderedEnumerable<FollowSummary> ordered = (Sort, Dir) switch
        {
            ("ToLookAt", "asc") => matched.OrderBy(s => s.ToLookAt),
            ("ToLookAt", _) => matched.OrderByDescending(s => s.ToLookAt),
            ("Searched", "asc") => matched.OrderBy(s => s.Follow.LastSearchedAt ?? DateTimeOffset.MinValue),
            ("Searched", _) => matched.OrderByDescending(s => s.Follow.LastSearchedAt ?? DateTimeOffset.MinValue),
            (_, "desc") => matched.OrderByDescending(byName, StringComparer.OrdinalIgnoreCase),
            _ => matched.OrderBy(byName, StringComparer.OrdinalIgnoreCase),
        };
        return ordered.ThenBy(byName, StringComparer.OrdinalIgnoreCase);
    }
}
