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
    public const string Bonus = "Bonus";
    public const string Backup = "Backup";
    public const string Seeding = "Seeding";
    public const string LibraryCheck = "Library check";

    /// <summary>The grabber name of downloads started from the Search page, as stored on their selections and events.</summary>
    public const string ManualSearchGrabber = "Manual search";

    /// <summary>Names a configured block can't have: the built-in jobs, and the Search page's grabber name.</summary>
    public static IReadOnlyList<string> Reserved { get; } = [Link, Clean, Download, Audiobookshelf, Follow, Upgrade, Bonus, Backup, Seeding, LibraryCheck, ManualSearchGrabber];
}

/// <summary>
/// Job names for per-entry `[[autograb]]`, `[[hardcover_list]]`, `[[file_list]]` and `[[bookmarks_list]]` jobs: the
/// entry's own `name` if set, else `autograb[&lt;index&gt;]`, `hardcover_list[&lt;index&gt;]`, `file_list[&lt;index&gt;]`
/// or `bookmarks_list` (there is only one).
/// </summary>
public static class AutoDownloaderJobNames
{
    public static string Autograb(AutograbEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"autograb[{index}]" : entry.Name;

    public static string HardcoverList(HardcoverListEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"hardcover_list[{index}]" : entry.Name;

    public static string FileList(FileListEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"file_list[{index}]" : entry.Name;

    public static string BookmarksList(BookmarksListEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Name) ? "bookmarks_list" : entry.Name;
}
