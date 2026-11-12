using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Following;

/// <summary>
/// A follow's buttons (<c>_FollowActions.cshtml</c>): change its mode, search now, stop following. On the Following page's
/// rows and at the top of a follow's own page.
/// </summary>
/// <param name="ReturnUrl">Where a button comes back to.</param>
/// <param name="StopReturnUrl">Where Stop following comes back to: not a follow's own page, which is gone by then.</param>
public sealed record FollowActionsViewModel(FollowEntity Follow, string ReturnUrl, string StopReturnUrl);
