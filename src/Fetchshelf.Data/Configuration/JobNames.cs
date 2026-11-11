namespace Fetchshelf.Data.Configuration;

/// <summary>The names of the built-in jobs, which no configured block may take.</summary>
public static class JobNames
{
    public const string Link = "Link";
    public const string Clean = "Clean";
    public const string Download = "Download";
    public const string Audiobookshelf = "Audiobookshelf";
    public const string Follow = "Follow";
    public const string Upgrade = "Upgrade";

    /// <summary>The grabber name of downloads started from the Search page, as stored on their selections and events.</summary>
    public const string ManualSearchGrabber = "Manual search";

    /// <summary>Names a configured block can't have: the built-in jobs, and the Search page's grabber name.</summary>
    public static IReadOnlyList<string> Reserved { get; } = [Link, Clean, Download, Audiobookshelf, Follow, Upgrade, ManualSearchGrabber];
}

/// <summary>
/// Job names for per-entry `[[autograb]]`, `[[snatchlist]]` and `[[hardcover_list]]` jobs: the entry's own `name` if
/// set, else `autograb[&lt;index&gt;]`, `snatchlist[&lt;index&gt;]` or `hardcover_list[&lt;index&gt;]`.
/// </summary>
public static class AutoDownloaderJobNames
{
    public static string Autograb(AutograbEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"autograb[{index}]" : entry.Name;

    public static string Snatchlist(SnatchlistEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"snatchlist[{index}]" : entry.Name;

    public static string HardcoverList(HardcoverListEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"hardcover_list[{index}]" : entry.Name;
}
