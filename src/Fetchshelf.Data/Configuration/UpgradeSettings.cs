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

    public bool DryRun { get; set; }

    /// <summary>Tried in order. Without any, `ConfigLoader` gives one `cost = "free"` block, so upgrades cost nothing.</summary>
    public List<GrabEntry> Grab { get; set; } = [];

    /// <summary>The longest wait between two searches for one book: a year.</summary>
    public const int MaxSearchAgainAfterDays = 365;
}
