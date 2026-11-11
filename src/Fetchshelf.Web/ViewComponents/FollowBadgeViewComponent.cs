using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Web.ViewComponents;

/// <summary>The count on the navigation's Following link: finds waiting to be downloaded or dismissed.</summary>
public sealed class FollowBadgeViewComponent(FetchshelfDbContext dbContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() =>
        View(await dbContext.FollowFinds.CountAsync(f => f.Status == FollowFindStatus.New || f.Status == FollowFindStatus.Wanted));
}
