using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// Every find waiting to be downloaded or dismissed, of every followed series and author, on one list: what the count on
/// the menu's Following link counts.
/// </summary>
public class ToLookAtModel(FetchshelfSettings settings, FetchshelfDbContext dbContext) : FindListPageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        if (settings.Follow is not { } followSettings)
        {
            return RedirectToPage("/Following/Index");
        }

        // Only what is to look at: the other states belong to each follow's own page.
        var query = ReadQuery() with { Show = FindShow.ToLookAt };
        List = await FindListViewModel.LoadAsync(
            dbContext, "/Following/ToLookAt", new Dictionary<string, string>(), followId: null, query, PageNumber,
            FollowQueries.WantedSides(followSettings.Grab), showsFollow: true, showsBook: true, showsShowFilter: false,
            ReturnUrl, HttpContext.RequestAborted);
        return Page();
    }
}
