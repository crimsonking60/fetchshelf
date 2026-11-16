using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.Configuration;

/// <summary>The Hardcover reading statuses a `[[hardcover_list]]` can follow.</summary>
public enum HardcoverListStatus
{
    WantToRead,
    CurrentlyReading,
    Read,
    Paused,
    DidNotFinish,
}

/// <summary>
/// One `[[hardcover_list]]` entry: the books with a reading status, or on one of the user's lists, on Hardcover,
/// searched on MAM with the `grab` blocks in order. Exactly one of <see cref="Status"/> and <see cref="List"/> is set
/// (enforced by `ConfigLoader`).
/// </summary>
public sealed class HardcoverListEntry
{
    public string? Name { get; set; }

    public HardcoverListStatus? Status { get; set; }

    /// <summary>The slug of one of the user's Hardcover lists, as in its address.</summary>
    public string? List { get; set; }

    /// <summary>Restricted to <see cref="FilterMainType.Audio"/>/<see cref="FilterMainType.Ebook"/> (enforced by `ConfigLoader`).</summary>
    public FilterMainType? PreferFormat { get; set; }

    /// <summary>qBittorrent category for everything this list downloads, as an autograb's `category`. Overrides `[[tag]]` rules.</summary>
    public string? Category { get; set; }

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
