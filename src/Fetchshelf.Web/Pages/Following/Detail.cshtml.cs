using System.Globalization;
using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// A followed series' or author's own page: its mode, when it was searched, its buttons, and what the Follow job found
/// for it, filtered, sorted and paged.
/// </summary>
public class DetailModel(FetchshelfSettings settings, FetchshelfDbContext dbContext) : FindListPageModel
{
    public FollowEntity? Follow { get; private set; }

    /// <summary>Its finds counted by state; null for an ignored series.</summary>
    public FollowSummary? Summary { get; private set; }

    /// <summary>When the Follow job searches it again, at the earliest; as on the Following page.</summary>
    public DateTimeOffset SearchAgainAfter(DateTimeOffset lastSearched) =>
        lastSearched + TimeSpan.FromHours(settings.Follow?.SearchAgainAfter ?? FollowSettings.DefaultSearchAgainAfterHours);

    public FollowActionsViewModel? Actions { get; private set; }

    /// <summary>A link to this page showing other finds, keeping the format, sort and page size.</summary>
    public Dictionary<string, string> RouteValuesFor(FindShow show) => List!.RouteValuesFor(List.Query with { Show = show });

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (settings.Follow is not { } followSettings)
        {
            return RedirectToPage("/Following/Index");
        }

        Follow = await dbContext.Follows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, HttpContext.RequestAborted);
        if (Follow is null)
        {
            return NotFound();
        }

        if (Follow.Mode == FollowMode.Ignore)
        {
            // Never searched, so nothing to list: the page only says so.
            return Page();
        }

        var wantedSides = FollowQueries.WantedSides(followSettings.Grab);
        Summary = (await FollowQueries.SummariesAsync(dbContext, wantedSides, HttpContext.RequestAborted)).FirstOrDefault(s => s.Follow.Id == id);
        Actions = new FollowActionsViewModel(Follow, ReturnUrl, Url.Page("/Following/Index")!);
        List = await FindListViewModel.LoadAsync(
            dbContext, "/Following/Detail", new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) }, id,
            ReadQuery(), PageNumber, wantedSides, showsFollow: false, showsBook: Follow.Kind == FollowKind.Series, showsShowFilter: true,
            ReturnUrl, HttpContext.RequestAborted);
        return Page();
    }
}
