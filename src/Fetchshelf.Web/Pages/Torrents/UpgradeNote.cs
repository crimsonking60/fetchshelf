using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>What the torrent pages say about a library copy a better format is possible for.</summary>
public static class UpgradeNote
{
    /// <summary>"mp3; m4b preferred" for a copy that could be better; null for one that couldn't.</summary>
    public static string? For(TorrentEntity torrent, FormatPreferenceLists preferenceLists) =>
        LibraryCopyRank.CouldBeUpgraded(torrent, preferenceLists) && LibraryCopyRank.BestFormat(torrent, preferenceLists) is { } best
            ? $"{LibraryCopyRank.ChosenFormat(torrent)}; {best} preferred"
            : null;
}
