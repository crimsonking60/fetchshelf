using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>A qBittorrent category a `[[library]]` picks torrents up by, with the libraries that use it.</summary>
public sealed record LibraryCategory(string Category, IReadOnlyList<string> Libraries)
{
    public string Label => Libraries.Count == 0 ? Category : $"{Category} ({string.Join(", ", Libraries)})";
}

/// <summary>The categories the torrent pages offer: those the configured libraries map to, in config order.</summary>
public static class LibraryCategories
{
    public static IReadOnlyList<LibraryCategory> For(FetchshelfSettings settings) =>
        settings.Library
            .Where(l => !string.IsNullOrWhiteSpace(l.Category))
            .GroupBy(l => l.Category!, StringComparer.Ordinal)
            .Select(g => new LibraryCategory(
                g.Key, g.Select(l => l.Name).OfType<string>().Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList()))
            .ToList();

    public static bool IsLibraryCategory(FetchshelfSettings settings, string? category) =>
        category is not null && For(settings).Any(c => c.Category == category);
}

/// <summary>The category dropdown on the torrent pages.</summary>
/// <param name="Current">Selected; offered as well when it is not a library category, so it is never changed by accident.</param>
/// <param name="Placeholder">A first, empty choice, for forms where no category is chosen yet.</param>
/// <param name="Form">The id of the form the dropdown belongs to, when it sits outside it.</param>
public sealed record LibraryCategorySelectViewModel(
    IReadOnlyList<LibraryCategory> Options, string? Current, string Id, string? Placeholder = null, string? Form = null)
{
    public bool CurrentIsOther => !string.IsNullOrEmpty(Current) && Options.All(o => o.Category != Current);
}
