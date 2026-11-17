namespace Fetchshelf.Core.Organizer;

/// <summary>How a torrent looks against MAM's seeding requirement, estimated from qBittorrent's seeding time.</summary>
public enum SeedingState
{
    /// <summary>Seeded at least the required time: it stays satisfied.</summary>
    Satisfied,

    /// <summary>Seeding now, but not yet for the required time, and still within the period.</summary>
    SeedingUnsatisfied,

    /// <summary>Not seeding now (stopped, errored, or no longer in qBittorrent), not yet satisfied, still within the period.</summary>
    NotSeedingUnsatisfied,

    /// <summary>Never reached the required time within the period: MAM likely counts it as a hit-and-run.</summary>
    PastThePeriod,
}

/// <summary>MAM's seeding requirement: <paramref name="SeedHours"/> of seeding within <paramref name="WithinDays"/> of the snatch.</summary>
public sealed record SeedingRequirement(int SeedHours, int WithinDays)
{
    public TimeSpan SeedTime => TimeSpan.FromHours(SeedHours);

    public TimeSpan Period => TimeSpan.FromDays(WithinDays);
}

/// <summary>
/// One torrent's estimate: its state, how long it has seeded, and, while unsatisfied, how much more it needs and by when.
/// </summary>
public sealed record SeedingEstimate(SeedingState State, TimeSpan Seeded, TimeSpan StillNeeded, DateTimeOffset Deadline)
{
    /// <summary>Not yet satisfied and still within the period: removing it from qBittorrent now risks a hit-and-run.</summary>
    public bool LooksUnsatisfied => State is SeedingState.SeedingUnsatisfied or SeedingState.NotSeedingUnsatisfied;
}

/// <summary>
/// Estimates, from qBittorrent's seeding time, whether a torrent has met MAM's seeding requirement. Fetchshelf can't ask
/// MAM per torrent: the snatch lists that say it aren't among the parts of MAM that may be automated. So this is an
/// estimate, checked against MAM's own counts on the Account page.
/// </summary>
public static class SeedingEstimator
{
    /// <summary>qBittorrent's states in which a torrent is seeding: uploading, or ready to with no one asking.</summary>
    public static bool IsSeeding(string qbittorrentState) => qbittorrentState is "uploading" or "stalledUP" or "forcedUP";

    /// <param name="seeded">How long it has seeded: qBittorrent's <c>seeding_time</c>.</param>
    /// <param name="snatchedAt">When the period started: when qBittorrent added the torrent.</param>
    /// <param name="seedingNow">Whether it is seeding now; false when it is no longer in qBittorrent.</param>
    /// <param name="previous">The state the last run found; a torrent once satisfied stays satisfied.</param>
    public static SeedingEstimate Estimate(
        TimeSpan seeded, DateTimeOffset snatchedAt, bool seedingNow, SeedingState? previous, DateTimeOffset now, SeedingRequirement requirement)
    {
        var deadline = snatchedAt + requirement.Period;
        if (previous == SeedingState.Satisfied || seeded >= requirement.SeedTime)
        {
            return new SeedingEstimate(SeedingState.Satisfied, seeded, TimeSpan.Zero, deadline);
        }

        var stillNeeded = requirement.SeedTime - seeded;
        var state = now > deadline ? SeedingState.PastThePeriod
            : seedingNow ? SeedingState.SeedingUnsatisfied
            : SeedingState.NotSeedingUnsatisfied;
        return new SeedingEstimate(state, seeded, stillNeeded, deadline);
    }

    /// <summary>"Seeded 41 of 72 hours; needs 31 more by 2026-12-14", or "Seeded 80 hours: satisfied".</summary>
    public static string Describe(SeedingEstimate estimate, SeedingRequirement requirement)
    {
        // Seeded rounds down and still needed rounds up, so neither claims more seeding than there was.
        var seeded = ((long)Math.Floor(estimate.Seeded.TotalHours)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return estimate.State switch
        {
            SeedingState.Satisfied => $"Seeded {seeded} hours: satisfied.",
            SeedingState.PastThePeriod =>
                $"Seeded {seeded} of {requirement.SeedHours} hours, and the {requirement.WithinDays} days to reach them ended on {Day(estimate.Deadline)}: MAM likely counts it as a hit-and-run. Seeding it the rest can still make up for it.",
            SeedingState.NotSeedingUnsatisfied =>
                $"Not seeding, and seeded {seeded} of {requirement.SeedHours} hours: seed it {Hours(estimate.StillNeeded)} more by {Day(estimate.Deadline)} to avoid a hit-and-run.",
            _ => $"Seeded {seeded} of {requirement.SeedHours} hours; needs {Hours(estimate.StillNeeded)} more by {Day(estimate.Deadline)}.",
        };
    }

    private static string Hours(TimeSpan time) => ((long)Math.Ceiling(time.TotalHours - 1e-9)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
