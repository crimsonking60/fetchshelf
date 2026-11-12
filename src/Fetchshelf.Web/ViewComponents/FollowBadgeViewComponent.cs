using Fetchshelf.Data;
using Fetchshelf.Data.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace Fetchshelf.Web.ViewComponents;

/// <summary>The count on the navigation's Following link: finds the Following page lists as waiting to be looked at.</summary>
public sealed class FollowBadgeViewComponent(FetchshelfDbContext dbContext, FetchshelfSettings settings) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(settings.Follow is { } follow ? await FollowQueries.WaitingCountAsync(dbContext, FollowQueries.WantedSides(follow.Grab)) : 0);
}
