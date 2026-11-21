namespace Fetchshelf.Web.Navigation;

/// <summary>A count on a navigation entry: what needs a look. <paramref name="Id"/> is the element id the tests and scripts know it by.</summary>
public sealed record NavBadge(string Id, int Count, string Title);

/// <summary>One link in the navigation: a page (as its Razor Pages path), its label and, when something needs attention, a badge.</summary>
public sealed record NavEntry(string Page, string Label, NavBadge? Badge = null);

/// <summary>A menu on the bar: related pages under one button. Its button carries the sum of its entries' badges.</summary>
public sealed record NavGroup(string Label, IReadOnlyList<NavEntry> Entries)
{
    public int BadgeTotal => Entries.Sum(e => e.Badge?.Count ?? 0);
}

/// <summary>What the navigation needs to know to decide on its entries and badges; every value is already read.</summary>
/// <param name="HasAccount">There is a MAM account to read, so the Account page has something to show.</param>
/// <param name="FollowConfigured">There is a <c>[follow]</c> section.</param>
/// <param name="ListsConfigured">At least one list (Hardcover, file or bookmarks) is configured.</param>
/// <param name="ErrorCount">Failing jobs and new errors; the Errors badge.</param>
/// <param name="ErrorDescription">The Errors badge's tooltip.</param>
/// <param name="FollowCount">Finds waiting to be looked at; the Following badge.</param>
/// <param name="LibraryFindings">Findings of the last Library check; the Library check badge.</param>
public sealed record NavInputs(
    bool HasAccount, bool FollowConfigured, bool ListsConfigured, int ErrorCount, string ErrorDescription, int FollowCount, int LibraryFindings);

/// <summary>
/// The main navigation: the pages used every day on the bar, related pages in menus, and the rare ones at the far end.
/// Entries that depend on the configuration are added when it has them, and a menu with no entries is left out.
/// </summary>
/// <param name="Primary">Entries on the bar itself, first.</param>
/// <param name="Groups">The menus, after the primary entries.</param>
/// <param name="Trailing">Entries at the far end of the bar.</param>
public sealed record NavModel(IReadOnlyList<NavEntry> Primary, IReadOnlyList<NavGroup> Groups, IReadOnlyList<NavEntry> Trailing)
{
    public const string SearchPage = "/Index";

    public static NavModel Build(NavInputs inputs)
    {
        // The work loop: find a book, queue it, watch it happen, see what went wrong.
        List<NavEntry> primary =
        [
            new(SearchPage, "Search"),
            new("/Torrents/Index", "Torrents"),
            new("/Selected/Index", "Selected"),
            new("/Jobs/Index", "Jobs"),
            new("/Errors/Index", "Errors", Badge("error-badge", inputs.ErrorCount, inputs.ErrorDescription)),
        ];

        // What is in the library and what is missing from it.
        List<NavEntry> library = [new("/Books/Index", "Books"), new("/Series/Index", "Series")];
        if (inputs.FollowConfigured)
        {
            library.Add(new("/Following/Index", "Following", Badge("follow-badge", inputs.FollowCount, FindsTitle(inputs.FollowCount))));
        }

        if (inputs.ListsConfigured)
        {
            library.Add(new("/Lists/Index", "Lists"));
        }

        library.Add(new("/Duplicates/Index", "Duplicates"));
        library.Add(new("/LibraryCheck/Index", "Library check", Badge("library-check-badge", inputs.LibraryFindings, "Findings of the last Library check")));

        // Looking back.
        List<NavEntry> insight = [new("/Events/Index", "Events"), new("/Statistics/Index", "Statistics")];
        if (inputs.HasAccount)
        {
            insight.Add(new("/MamApi/Index", "MAM API"));
        }

        List<NavEntry> trailing = [];
        if (inputs.HasAccount)
        {
            trailing.Add(new("/Account/Index", "Account"));
        }

        trailing.Add(new("/Config/Index", "Config"));

        List<NavGroup> groups = [new("Library", library), new("Insight", insight)];
        return new NavModel(primary, groups.Where(g => g.Entries.Count > 0).ToList(), trailing);
    }

    /// <summary>
    /// Whether <paramref name="currentPage"/> (a Razor Pages path such as "/Torrents/Detail") belongs to the entry for
    /// <paramref name="navPage"/>. Search lives at "/Index"; every other entry owns the folder it points into, so
    /// the Torrents entry stays lit on a torrent's own page.
    /// </summary>
    public static bool IsCurrent(string currentPage, string navPage) =>
        navPage == SearchPage
            ? currentPage == SearchPage
            : currentPage.StartsWith(navPage[..(navPage.LastIndexOf('/') + 1)], StringComparison.Ordinal);

    private static NavBadge? Badge(string id, int count, string title) => count > 0 ? new NavBadge(id, count, title) : null;

    private static string FindsTitle(int count) => $"{count} {(count == 1 ? "find" : "finds")} to look at";
}
