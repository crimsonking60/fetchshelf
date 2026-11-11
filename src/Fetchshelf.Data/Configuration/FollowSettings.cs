using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The optional `[follow]` section: how the Follow job searches MAM for followed series and authors, and what it may
/// take. Which series and authors are followed is chosen in the web UI, not here. Absent (null on
/// <see cref="FetchshelfSettings.Follow"/>) means no Follow job and no follow buttons.
/// </summary>
public sealed class FollowSettings
{
    /// <summary>Minutes between runs; unset means the top-level `search_interval`, 0 means only from the Jobs page.</summary>
    public int? SearchInterval { get; set; }

    /// <summary>MAM searches one run makes at most: one per followed series or author.</summary>
    public int SearchesPerRun { get; set; } = DefaultSearchesPerRun;

    /// <summary>Hours before the same series or author is searched again.</summary>
    public int SearchAgainAfter { get; set; } = DefaultSearchAgainAfterHours;

    /// <summary>Restricted to <see cref="FilterMainType.Audio"/>/<see cref="FilterMainType.Ebook"/> (enforced by `ConfigLoader`).</summary>
    public FilterMainType? PreferFormat { get; set; }

    /// <summary>qBittorrent category for everything the job downloads, as an autograb's `category`. Overrides `[[tag]]` rules.</summary>
    public string? Category { get; set; }

    public bool DryRun { get; set; }

    public List<GrabEntry> Grab { get; set; } = [];

    public const int DefaultSearchesPerRun = 10;

    public const int DefaultSearchAgainAfterHours = 24;

    /// <summary>The longest wait between two searches of one series or author: 30 days.</summary>
    public const int MaxSearchAgainAfterHours = 720;
}
