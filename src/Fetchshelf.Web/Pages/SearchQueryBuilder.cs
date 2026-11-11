using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// What the Search page asks MAM, from its query-string choices. Every choice has a default that gives the search the
/// page has always made, and a value it doesn't know (from a hand-made or old link) counts as the default.
/// </summary>
public static class SearchQueryBuilder
{
    /// <summary>Results per page: MAM's largest page.</summary>
    public const int PageSize = 100;

    public const string DefaultSort = "relevance";

    public const string DefaultType = "all";

    /// <summary>The type that keeps only torrents the account hasn't snatched; MAM can't be asked for that, so the page filters.</summary>
    public const string NotSnatchedType = "not_snatched";

    /// <summary>The fields a search can look in, as their `In` values and labels, in the order the page shows them.</summary>
    public static IReadOnlyList<(string Value, string Label)> FieldOptions { get; } =
    [
        ("title", "Title"),
        ("author", "Author"),
        ("series", "Series"),
        ("narrator", "Narrator"),
        ("description", "Description"),
        ("tags", "Tags"),
        ("filenames", "File names"),
    ];

    public static IReadOnlyList<string> DefaultFields { get; } = ["title", "author"];

    public static IReadOnlyList<(string Value, string Label, MamSortOrder Order)> SortOptions { get; } =
    [
        (DefaultSort, "Best match", MamSortOrder.NewestFirst),
        ("newest", "Newest", MamSortOrder.NewestByDate),
        ("oldest", "Oldest", MamSortOrder.OldestFirst),
        ("seeders", "Most seeders", MamSortOrder.MostSeeders),
        ("snatched", "Most snatched", MamSortOrder.MostSnatches),
        ("size", "Largest", MamSortOrder.LargestFirst),
        ("title", "Title", MamSortOrder.TitleAsc),
    ];

    public static IReadOnlyList<(string Value, string Label)> TypeOptions { get; } =
    [
        (DefaultType, "All torrents"),
        ("freeleech", "Freeleech"),
        ("free_or_vip", "Freeleech or VIP"),
        ("vip", "VIP"),
        ("bookmarks", "My bookmarks"),
        (NotSnatchedType, "Not snatched by me"),
    ];

    /// <summary>The known `In` values among <paramref name="names"/>, in the page's order; the default when there are none.</summary>
    public static IReadOnlyList<string> Fields(IEnumerable<string?>? names)
    {
        var chosen = (names ?? []).OfType<string>().Select(n => n.Trim().ToLowerInvariant()).ToHashSet();
        var known = FieldOptions.Select(o => o.Value).Where(chosen.Contains).ToList();
        return known.Count > 0 ? known : DefaultFields;
    }

    /// <summary>The sort's own value when it is a known one, else <see cref="DefaultSort"/>.</summary>
    public static string Sort(string? sort) =>
        SortOptions.Any(o => o.Value == sort?.ToLowerInvariant()) ? sort!.ToLowerInvariant() : DefaultSort;

    /// <summary>The type's own value when it is a known one, else <see cref="DefaultType"/>.</summary>
    public static string Type(string? type) =>
        TypeOptions.Any(o => o.Value == type?.ToLowerInvariant()) ? type!.ToLowerInvariant() : DefaultType;

    /// <summary>A page number of at least 1.</summary>
    public static int PageNumber(int page) => Math.Max(1, page);

    public static MamSearchQuery Build(
        string? text, IEnumerable<string?>? fields, string? sort, string? type, int page, IReadOnlyList<Language> languages)
    {
        var hasText = !string.IsNullOrWhiteSpace(text);
        var chosenType = Type(type);
        return new MamSearchQuery
        {
            Text = hasText ? text : null,
            SearchFields = hasText ? Fields(fields).Select(ToField).ToList() : [],
            Target = chosenType == "bookmarks" ? MamSearchTarget.Bookmarks : MamSearchTarget.New,
            Kind = chosenType switch
            {
                "freeleech" => MamSearchKind.Freeleech,
                "free_or_vip" => MamSearchKind.FreeOrVip,
                "vip" => MamSearchKind.Vip,
                _ => null,
            },
            SortOrder = SortOptions.First(o => o.Value == Sort(sort)).Order,
            PageOffset = (PageNumber(page) - 1) * (long)PageSize,
            PerPage = PageSize,
            ReturnDownloadLink = true,
            LanguageIds = languages.Select(language => (int)language).ToList(),
        };
    }

    private static MamSearchField ToField(string name) =>
        AutograbSearchOptions.TryParseSearchField(name, out var field)
            ? field
            : throw new InvalidOperationException($"'{name}' is in the field list but not a search field.");
}
