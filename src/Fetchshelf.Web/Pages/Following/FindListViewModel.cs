using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// One page of a list of finds (<c>_FindList.cshtml</c>), as a follow's page and the To look at page show it: the
/// filters, the rows with their status badges, the buttons and tick boxes, and the pager.
/// </summary>
public sealed class FindListViewModel
{
    /// <summary>The page the list is on, for its filter, sort and page links.</summary>
    public required string Page { get; init; }

    /// <summary>Route values of the page itself, such as a follow's id, that every link keeps.</summary>
    public required IReadOnlyDictionary<string, string> PageRouteValues { get; init; }

    public required FindListQuery Query { get; init; }

    public required IReadOnlyList<FindRow> Rows { get; init; }

    public required PagerViewModel Pager { get; init; }

    /// <summary>Where each find's torrent stands in Fetchshelf, by MAM id; finds never selected have none.</summary>
    public required IReadOnlyDictionary<long, MamResultStatus> Statuses { get; init; }

    /// <summary>Whether the rows are of several follows, so each says whose it is.</summary>
    public required bool ShowsFollow { get; init; }

    /// <summary>Whether the rows have series entries to show and sort by: a series' finds, or several follows'.</summary>
    public required bool ShowsBook { get; init; }

    /// <summary>Whether the Show dropdown is offered; the To look at page only lists what is to look at.</summary>
    public required bool ShowsShowFilter { get; init; }

    /// <summary>Whether a find can be not free (<see cref="FollowQueries.CanBeNotFree"/>), so that is a choice under Show.</summary>
    public required bool ShowsNotFree { get; init; }

    /// <summary>Where a button on the list comes back to: this page, with its filters, sort and page.</summary>
    public required string ReturnUrl { get; init; }

    /// <summary>A link's route values: the page's own and the query's.</summary>
    public Dictionary<string, string> RouteValuesFor(FindListQuery query) =>
        new(PageRouteValues.Concat(query.ToRouteValues()));

    /// <summary>
    /// The status badge for a find whose torrent is selected, downloading, downloaded or linked, as the Search page shows
    /// it: it asks again by itself and starts the Link job once qBittorrent has finished. Null for a find never selected,
    /// which keeps its own label.
    /// </summary>
    public SearchResultStatusViewModel? StatusBadge(FollowFindEntity find) =>
        Statuses.TryGetValue(find.MamId, out var status) && status != MamResultStatus.None
            ? new SearchResultStatusViewModel(find.MamId, status)
            : null;

    /// <summary>Whether a find can still be downloaded or dismissed.</summary>
    public static bool IsWaiting(FollowFindEntity find) => find.Status is FollowFindStatus.New or FollowFindStatus.Wanted;

    /// <summary>
    /// Reads, sorts and pages the finds the query asks for, of one follow or of every followed one. What is to look at
    /// includes the finds downloaded and not linked yet: a find downloaded from the list stays on it, its status badge
    /// moving on (and starting the Link job) as on the Search page, until it is in the library.
    /// </summary>
    public static async Task<FindListViewModel> LoadAsync(
        FetchshelfDbContext dbContext, string page, IReadOnlyDictionary<string, string> pageRouteValues, int? followId,
        FindListQuery query, int pageNumber, IReadOnlySet<string> wantedSides, bool showsFollow, bool showsBook, bool showsShowFilter,
        bool showsNotFree, string returnUrl, CancellationToken cancellationToken)
    {
        var rows = await FollowQueries.FindsAsync(dbContext, followId, query.Show, query.Format, wantedSides, cancellationToken);
        if (query.Show == FindShow.ToLookAt)
        {
            rows.AddRange(await OnTheirWayAsync(dbContext, followId, query.Format, wantedSides, cancellationToken));
        }

        var (shown, number, totalPages) = Paging.Slice(query.Order(rows), rows.Count, pageNumber, query.PageSize);
        var statuses = await MamResultStatuses.LoadAsync(dbContext, shown.Select(r => r.Find.MamId).Distinct().ToList());
        var routeValues = new Dictionary<string, string>(pageRouteValues.Concat(query.ToRouteValues()));
        return new FindListViewModel
        {
            Page = page,
            PageRouteValues = pageRouteValues,
            Query = query,
            Rows = shown,
            Pager = new PagerViewModel(page, routeValues, number, totalPages, shown.Count, rows.Count, query.PageSize, "find-pager", "Pages of finds"),
            Statuses = statuses,
            ShowsFollow = showsFollow,
            ShowsBook = showsBook,
            ShowsShowFilter = showsShowFilter,
            ShowsNotFree = showsNotFree,
            ReturnUrl = returnUrl,
        };
    }

    /// <summary>The selected finds whose torrent is selected, downloading or downloaded, but not linked yet.</summary>
    private static async Task<List<FindRow>> OnTheirWayAsync(
        FetchshelfDbContext dbContext, int? followId, string? format, IReadOnlySet<string> wantedSides, CancellationToken cancellationToken)
    {
        var selected = await FollowQueries.FindsAsync(dbContext, followId, FindShow.Selected, format, wantedSides, cancellationToken);
        var statuses = await MamResultStatuses.LoadAsync(dbContext, selected.Select(r => r.Find.MamId).Distinct().ToList());
        return selected
            .Where(r => statuses.GetValueOrDefault(r.Find.MamId) is MamResultStatus.Selected or MamResultStatus.Downloading or MamResultStatus.Downloaded)
            .ToList();
    }
}
