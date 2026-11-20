using System.Collections.Immutable;

namespace Fetchshelf.Web.Pages;

/// <summary>Remembers which pages' explanations were shown in full, so that later visits can fold them to one line.</summary>
public static class PageExplanationCookie
{
    public const string Name = "fs-seen-pages";

    /// <summary>The most pages remembered; the cookie stays well under the size limit.</summary>
    private const int MaxPages = 30;

    public static string Key(HttpRequest request) => request.Path.Value?.Trim('/').ToLowerInvariant() is { Length: > 0 } path ? path : "home";

    public static ImmutableList<string> Seen(HttpRequest request) =>
        (request.Cookies[Name] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToImmutableList();

    public static void Remember(HttpResponse response, IReadOnlyList<string> seen, string key)
    {
        var pages = seen.Append(key).TakeLast(MaxPages);
        response.Cookies.Append(Name, string.Join(',', pages), new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
        });
    }
}
