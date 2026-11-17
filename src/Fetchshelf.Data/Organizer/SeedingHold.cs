using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// `[seeding]` `warn_before_removal`: the Clean job holds `on_cleaned` back for a copy that looks unsatisfied, and the
/// Seeding job applies it once the copy is satisfied. Registered with the requirement only when it is on.
/// </summary>
public sealed record SeedingHold(SeedingRequirement Requirement)
{
    /// <summary>Why `on_cleaned` waits for <paramref name="torrent"/>; null when it doesn't: satisfied, or never estimated.</summary>
    public string? HoldsBack(TorrentEntity torrent) =>
        Enum.TryParse<SeedingState>(torrent.SeedingState, out var state) && state != SeedingState.Satisfied
            ? $"it has seeded {Math.Floor(TimeSpan.FromSeconds(torrent.SeedingSeconds ?? 0).TotalHours)} of the {Requirement.SeedHours} hours MAM requires, so it is applied once it is satisfied"
            : null;
}
