namespace Fetchshelf.Web;

/// <summary>The user-selectable UI themes. Each id matches a <c>[data-theme]</c> block in wwwroot/css/themes.css.</summary>
public static class Themes
{
    public const string CookieName = "fs-theme";

    /// <summary>The choice of a visitor who has made none: the system's own preference, Dark Warm when it is dark and Light Warm when it is light (see themes.css).</summary>
    public const string System = "system";

    public const string Default = System;

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        (System, "Match system"),
        ("dark-warm", "Dark Warm"),
        ("dark-cool", "Dark Cool"),
        ("light-warm", "Light Warm"),
        ("light-cool", "Light Cool"),
    ];

    /// <summary>Returns <paramref name="id"/> if it names a known theme, else the default (a stale or tampered cookie must not reach the markup).</summary>
    public static string Resolve(string? id) =>
        All.Any(t => t.Id == id) ? id! : Default;

    public static void MapThemeEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/theme", (string? name, string? returnUrl, HttpContext context) =>
        {
            context.Response.Cookies.Append(CookieName, Resolve(name), new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
            });
            return Results.LocalRedirect(IsLocalPath(returnUrl) ? returnUrl! : "/");
        });

    private static bool IsLocalPath(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
