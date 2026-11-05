using System.Globalization;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// Remembers the page size chosen under each paged list in a cookie of its own, so a list opened without one in the
/// address (from the menu, say) comes back at the size last chosen there.
/// </summary>
public static class PageSizeCookie
{
    public static string NameFor(string list) => $"fs-page-size-{list}";

    /// <summary>
    /// The page size to use. One in the address wins, and is remembered when it is one of the dropdown's choices;
    /// without one, the remembered choice applies, else <paramref name="pageSize"/>, the list's default.
    /// </summary>
    public static int Apply(HttpContext context, string list, int pageSize)
    {
        var name = NameFor(list);
        if (context.Request.Query.ContainsKey("PageSize"))
        {
            if (IsChoice(pageSize))
            {
                context.Response.Cookies.Append(name, pageSize.ToString(CultureInfo.InvariantCulture), new CookieOptions
                {
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(365),
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,
                });
            }

            return pageSize;
        }

        return int.TryParse(context.Request.Cookies[name], NumberStyles.None, CultureInfo.InvariantCulture, out var remembered)
            && IsChoice(remembered)
                ? remembered
                : pageSize;
    }

    private static bool IsChoice(int pageSize) => Paging.PageSizeOptions.Any(o => o.Size == pageSize);
}
