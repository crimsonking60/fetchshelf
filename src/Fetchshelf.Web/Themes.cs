namespace Fetchshelf.Web;

/// <summary>The user-selectable UI themes. Each id matches a <c>[data-theme]</c> block in wwwroot/css/themes.css.</summary>
public static class Themes
{
    public const string CookieName = "fs-theme";
    public const string Default = "dark-warm";

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
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
