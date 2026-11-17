using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// The optional `[seeding]` section: the Seeding job, which estimates from qBittorrent's seeding time which of
/// Fetchshelf's torrents still need seeding to satisfy MAM.
/// </summary>
public sealed class SeedingSettings
{
    /// <summary>Hours of seeding MAM requires before a snatch is satisfied.</summary>
    public int SeedHours { get; set; } = 72;

    /// <summary>Days after the snatch in which <see cref="SeedHours"/> must be reached.</summary>
    public int WithinDays { get; set; } = 30;

    /// <summary>Minutes between runs of the Seeding job; 0 runs it only from the Jobs page.</summary>
    public int Interval { get; set; } = 60;

    /// <summary>A qBittorrent tag put on torrents that look unsatisfied and taken off once they look satisfied.</summary>
    public string? TagUnsatisfied { get; set; }

    /// <summary>Warn on Unlink, and hold back the Clean job's `on_cleaned`, for torrents that look unsatisfied.</summary>
    public bool WarnBeforeRemoval { get; set; } = true;

    public SeedingRequirement Requirement => new(SeedHours, WithinDays);
}
