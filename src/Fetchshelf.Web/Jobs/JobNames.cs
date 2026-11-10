using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Jobs;

internal static class JobNames
{
    public const string Link = "Link";
    public const string Clean = "Clean";
    public const string Download = "Download";
    public const string Audiobookshelf = "Audiobookshelf";
}

/// <summary>Job names for per-entry `[[autograb]]`/`[[snatchlist]]` jobs: the entry's own `name` if set, else `autograb[&lt;index&gt;]`/`snatchlist[&lt;index&gt;]`</summary>
internal static class AutoDownloaderJobNames
{
    public static string Autograb(AutograbEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"autograb[{index}]" : entry.Name;

    public static string Snatchlist(SnatchlistEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"snatchlist[{index}]" : entry.Name;

    public static string HardcoverList(HardcoverListEntry entry, int index) =>
        string.IsNullOrWhiteSpace(entry.Name) ? $"hardcover_list[{index}]" : entry.Name;
}
