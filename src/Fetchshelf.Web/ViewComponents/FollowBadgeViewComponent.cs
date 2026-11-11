using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Microsoft.AspNetCore.Mvc;

namespace Fetchshelf.Web.ViewComponents;

/// <summary>The count on the navigation's Following link: finds the Following page lists as waiting to be looked at.</summary>
public sealed class FollowBadgeViewComponent(FetchshelfDbContext dbContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() => View(await FollowQueries.WaitingCountAsync(dbContext));
}
