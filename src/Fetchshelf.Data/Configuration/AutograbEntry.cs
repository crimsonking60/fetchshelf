using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One `[[autograb]]` entry. Inherits the shared torrent filter's fields
/// directly as its own top-level YAML keys, matching the config's flattened
/// shape, rather than nesting them under a sub-key.
/// </summary>
public sealed class AutograbEntry : SharedFilterConfig
{
    public AutograbType? Type { get; set; }

    public ConfiguredCost Cost { get; set; } = ConfiguredCost.Free;

    /// <summary>`description`: a line of the user's own, shown under the job's name on the Jobs page.</summary>
    public string? Description { get; set; }

    public string? Query { get; set; }

    public List<string> SearchIn { get; set; } = [];

    public string? SortBy { get; set; }

    public int? MaxPages { get; set; }

    public int? SearchInterval { get; set; }

    public int? UnsatBuffer { get; set; }

    public int? WedgeBuffer { get; set; }

    public int? MaxActiveDownloads { get; set; }

    public bool DryRun { get; set; }

    public bool MarkRemoved { get; set; }

    public string? Category { get; set; }

    /// <summary>`client`: the `name` of the `[[qbittorrent]]` that receives what this block downloads; unset means the first one.</summary>
    public string? Client { get; set; }

    /// <summary>`save_path`: the folder qBittorrent saves what this block downloads in, as qBittorrent sees it; unset leaves it to qBittorrent.</summary>
    public string? SavePath { get; set; }

    /// <summary>
    /// Default 50 for `bookmarks`/`freeleech`/`mine`, 1 otherwise, applied when
    /// `max_pages` is not configured.
    /// </summary>
    public int EffectiveMaxPages => MaxPages ?? (Type?.Kind is "bookmarks" or "freeleech" or "mine" ? 50 : 1);
}
