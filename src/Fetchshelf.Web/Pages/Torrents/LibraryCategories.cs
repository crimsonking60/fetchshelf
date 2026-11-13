using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>
/// A qBittorrent category the torrent pages offer: one a `[[library]]` picks torrents up by, with the libraries that use
/// it, or one of `extra_categories`, which no library uses.
/// </summary>
public sealed record LibraryCategory(string Category, IReadOnlyList<string> Libraries, bool Extra = false)
{
    public string Label => Extra ? $"{Category} (no library)"
        : Libraries.Count == 0 ? Category
        : $"{Category} ({string.Join(", ", Libraries)})";
}

/// <summary>
/// The categories the torrent pages offer: those the configured libraries map to, in config order, then the
/// `extra_categories` no library already uses.
/// </summary>
public static class LibraryCategories
{
    public static IReadOnlyList<LibraryCategory> For(FetchshelfSettings settings)
    {
        var libraries = settings.Library
            .Where(l => !string.IsNullOrWhiteSpace(l.Category))
            .GroupBy(l => l.Category!, StringComparer.Ordinal)
            .Select(g => new LibraryCategory(
                g.Key, g.Select(l => l.Name).OfType<string>().Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList()))
            .ToList();
        var extras = settings.ExtraCategories
            .Where(c => libraries.All(l => l.Category != c))
            .Distinct(StringComparer.Ordinal)
            .Select(c => new LibraryCategory(c, [], Extra: true));
        return [.. libraries, .. extras];
    }

    /// <summary>Whether a library picks torrents up by <paramref name="category"/>.</summary>
    public static bool IsLibraryCategory(FetchshelfSettings settings, string? category) =>
        category is not null && For(settings).Any(c => c.Category == category && !c.Extra);

    /// <summary>Whether the torrent pages offer <paramref name="category"/>: a library's, or one of `extra_categories`.</summary>
    public static bool IsOffered(FetchshelfSettings settings, string? category) =>
        category is not null && For(settings).Any(c => c.Category == category);
}

/// <summary>The category dropdown on the torrent pages.</summary>
/// <param name="Current">Selected; offered as well when it is not one of the options, so it is never changed by accident.</param>
/// <param name="Placeholder">A first, empty choice, for forms where no category is chosen yet.</param>
/// <param name="Form">The id of the form the dropdown belongs to, when it sits outside it.</param>
public sealed record LibraryCategorySelectViewModel(
    IReadOnlyList<LibraryCategory> Options, string? Current, string Id, string? Placeholder = null, string? Form = null)
{
    public bool CurrentIsOther => !string.IsNullOrEmpty(Current) && Options.All(o => o.Category != Current);
}
