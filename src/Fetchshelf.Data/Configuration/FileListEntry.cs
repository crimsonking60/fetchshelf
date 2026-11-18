using Fetchshelf.Core.Metadata;
using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One `[[file_list]]` entry: the books in a CSV or text file, such as a Goodreads or StoryGraph export, searched on MAM
/// with the `grab` blocks in order, like a `[[hardcover_list]]`.
/// </summary>
public sealed class FileListEntry
{
    public string? Name { get; set; }

    /// <summary>The file, as configured; a relative path is relative to the config file's folder (<see cref="FullPath"/>).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Keeps only the rows on this shelf, for a file with a shelf column (Goodreads' `Exclusive Shelf`, StoryGraph's `Read Status`).</summary>
    public string? Shelf { get; set; }

    /// <summary>Restricted to <see cref="FilterMainType.Audio"/>/<see cref="FilterMainType.Ebook"/> (enforced by `ConfigLoader`).</summary>
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

    /// <summary><see cref="Path"/> made absolute against the config file's folder; set by `ConfigLoader`.</summary>
    [YamlIgnore]
    public string FullPath { get; set; } = string.Empty;
}
