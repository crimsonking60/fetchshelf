namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The optional `[upgrade]` section: the Upgrade job, which searches MAM for better formats of books in the library.
/// Absent (null on <see cref="FetchshelfSettings.Upgrade"/>) means no Upgrade job.
/// </summary>
public sealed class UpgradeSettings
{
    /// <summary>Minutes between runs; unset means the top-level `search_interval`, 0 means only from the Jobs page.</summary>
    public int? SearchInterval { get; set; }

    /// <summary>MAM searches one run makes at most: one per book.</summary>
    public int SearchesPerRun { get; set; } = 20;

    /// <summary>Days before the same book is searched again.</summary>
    public int SearchAgainAfter { get; set; } = 30;

    /// <summary>Only books in these `[[library]]` names; every library when empty.</summary>
    public List<string> Libraries { get; set; } = [];

    /// <summary>`client`: the `name` of the `[[qbittorrent]]` that receives the better copies; unset means the first one.</summary>
    public string? Client { get; set; }

    /// <summary>`save_path`: the folder qBittorrent saves the better copies in, as qBittorrent sees it; unset leaves it to qBittorrent.</summary>
    public string? SavePath { get; set; }

    public bool DryRun { get; set; }

    /// <summary>Overrides the top-level `unsat_buffer` for what this block selects, as an autograb's does.</summary>
    public int? UnsatBuffer { get; set; }

    /// <summary>Overrides the top-level `wedge_buffer` for what this block selects, as an autograb's does.</summary>
    public int? WedgeBuffer { get; set; }

    /// <summary>The most selections this block may have waiting to be downloaded; unset means no cap.</summary>
    public int? MaxActiveDownloads { get; set; }

    /// <summary>Tried in order. Without any, `ConfigLoader` gives one `cost = "free"` block, so upgrades cost nothing.</summary>
    public List<GrabEntry> Grab { get; set; } = [];

    /// <summary>The longest wait between two searches for one book: a year.</summary>
    public const int MaxSearchAgainAfterDays = 365;
}
