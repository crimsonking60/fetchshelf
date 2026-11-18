using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The `[[bookmarks_list]]` entry: the torrents bookmarked on MAM, shown on the Lists page with what happened to each, and
/// selected through the `grab` blocks in order, like a `[[hardcover_list]]`'s books. At most one (enforced by
/// `ConfigLoader`), as there is one bookmark list.
/// </summary>
public sealed class BookmarksListEntry
{
    public string? Name { get; set; }

    /// <summary>Refused by `ConfigLoader`: a bookmark is a torrent, so of one format already. Read only to say so.</summary>
    public FilterMainType? PreferFormat { get; set; }

    /// <summary>qBittorrent category for everything this list downloads, as an autograb's `category`. Overrides `[[tag]]` rules.</summary>
    public string? Category { get; set; }

    /// <summary>`client`: the `name` of the `[[qbittorrent]]` that receives everything this list downloads; unset means the first one.</summary>
    public string? Client { get; set; }

    /// <summary>`save_path`: the folder qBittorrent saves everything this list downloads in, as qBittorrent sees it; unset leaves it to qBittorrent.</summary>
    public string? SavePath { get; set; }

    public List<GrabEntry> Grab { get; set; } = [];

    public int? SearchInterval { get; set; }

    /// <summary>Overrides the top-level `unsat_buffer` for what this block selects, as an autograb's does.</summary>
    public int? UnsatBuffer { get; set; }

    /// <summary>Overrides the top-level `wedge_buffer` for what this block selects, as an autograb's does.</summary>
    public int? WedgeBuffer { get; set; }

    /// <summary>The most selections this block may have waiting to be downloaded; unset means no cap.</summary>
    public int? MaxActiveDownloads { get; set; }

    public bool DryRun { get; set; }
}
