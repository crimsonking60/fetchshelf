using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages;

/// <summary>
/// The Search page's choices, each made known and in range once (<see cref="SearchQueryBuilder.Choices"/>): the links,
/// the heading and the MAM query all read these, so they can't disagree.
/// </summary>
public sealed record SearchChoices(string? Text, IReadOnlyList<string> Fields, string Sort, string Type, int PageNumber)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>The number of the first result on the page, from 1.</summary>
    public long FirstResultNumber => (PageNumber - 1L) * SearchQueryBuilder.PageSize + 1;

    /// <summary>Whether MAM found more than the pages up to and including this one hold.</summary>
    public bool HasPageAfter(int found) => (long)PageNumber * SearchQueryBuilder.PageSize < found;
}

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
        ("bookmarks", "Bookmarked on MAM"),
        (NotSnatchedType, "Not snatched yet"),
    ];

    /// <summary>The known `In` values among <paramref name="names"/>, in the page's order; the default when there are none.</summary>
    public static IReadOnlyList<string> Fields(IEnumerable<string?>? names)
    {
        var chosen = (names ?? []).OfType<string>().Select(n => n.Trim().ToLowerInvariant()).ToHashSet();
        var known = FieldOptions.Select(o => o.Value).Where(chosen.Contains).ToList();
        return known.Count > 0 ? known : DefaultFields;
    }

    /// <summary>The sort's own value when it is a known one, else <see cref="DefaultSort"/>.</summary>
    public static string Sort(string? sort) => Known(SortOptions.Select(o => o.Value), sort, DefaultSort);

    /// <summary>The type's own value when it is a known one, else <see cref="DefaultType"/>.</summary>
    public static string Type(string? type) => Known(TypeOptions.Select(o => o.Value), type, DefaultType);

    private static string Known(IEnumerable<string> values, string? value, string fallback) =>
        value?.Trim().ToLowerInvariant() is { } lower && values.Contains(lower) ? lower : fallback;

    /// <summary>The last page a link can ask for: a million results in, far past any real search.</summary>
    public const int MaxPageNumber = 10_000;

    /// <summary>A page number from 1 to <see cref="MaxPageNumber"/>.</summary>
    public static int PageNumber(int page) => Math.Clamp(page, 1, MaxPageNumber);

    /// <summary>The page's choices as they came in the address, each made known and in range.</summary>
    public static SearchChoices Choices(string? text, IEnumerable<string?>? fields, string? sort, string? type, int page) =>
        new(text, Fields(fields), Sort(sort), Type(type), PageNumber(page));

    public static MamSearchQuery Build(SearchChoices choices, IReadOnlyList<Language> languages)
    {
        return new MamSearchQuery
        {
            Text = choices.HasText ? choices.Text : null,
            SearchFields = choices.HasText ? choices.Fields.Select(ToField).ToList() : [],
            Target = choices.Type == "bookmarks" ? MamSearchTarget.Bookmarks : MamSearchTarget.New,
            Kind = choices.Type switch
            {
                "freeleech" => MamSearchKind.Freeleech,
                "free_or_vip" => MamSearchKind.FreeOrVip,
                "vip" => MamSearchKind.Vip,
                _ => null,
            },
            SortOrder = SortOptions.First(o => o.Value == choices.Sort).Order,
            PageOffset = (choices.PageNumber - 1L) * PageSize,
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
