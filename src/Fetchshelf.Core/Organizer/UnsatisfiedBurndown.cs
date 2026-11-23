namespace Fetchshelf.Core.Organizer;

/// <summary>What the Seeding job last stored for one torrent: its state, how long it had seeded, and when it was checked.</summary>
/// <param name="SnatchedAt">When the period started: when qBittorrent added the torrent.</param>
public sealed record SeedingSnapshot(SeedingState State, TimeSpan Seeded, DateTimeOffset SnatchedAt, DateTimeOffset CheckedAt);

/// <summary>One hour of the burndown.</summary>
/// <param name="Unsatisfied">Torrents that look unsatisfied at <paramref name="At"/>.</param>
/// <param name="Satisfied">Torrents satisfied since the row before.</param>
/// <param name="PastThePeriod">Torrents whose period ended since the row before without enough seeding.</param>
public sealed record BurndownRow(DateTimeOffset At, int Unsatisfied, int Satisfied, int PastThePeriod)
{
    /// <summary>Fewer torrents look unsatisfied than in the row before.</summary>
    public bool Fewer => Satisfied + PastThePeriod > 0;
}

/// <summary>
/// How many torrents will look unsatisfied, hour by hour, from the Seeding job's last estimates: a torrent seeding now
/// is taken to keep seeding, so it is satisfied once it has seeded what it still needs, and a torrent not seeding stays
/// unsatisfied until its period ends. Either drops out at the end of its period, as MAM likely counts it as a
/// hit-and-run then.
/// </summary>
public static class UnsatisfiedBurndown
{
    /// <summary>How many hours ahead the burndown looks.</summary>
    public const int Hours = 72;

    /// <summary>Rows for now (to the minute) and each of the next <see cref="Hours"/> hours.</summary>
    public static IReadOnlyList<BurndownRow> Build(IEnumerable<SeedingSnapshot> torrents, DateTimeOffset now, SeedingRequirement requirement)
    {
        var start = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerMinute), now.Offset);
        var ends = torrents.Where(t => t.State is SeedingState.SeedingUnsatisfied or SeedingState.NotSeedingUnsatisfied)
            .Select(t => End(t, requirement))
            .ToList();

        var rows = new List<BurndownRow>(Hours + 1);
        for (var hour = 0; hour <= Hours; hour++)
        {
            var at = start.AddHours(hour);
            var previous = hour == 0 ? (DateTimeOffset?)null : rows[^1].At;
            int unsatisfied = 0, satisfied = 0, pastThePeriod = 0;
            foreach (var (endsAt, bySeeding) in ends)
            {
                if (StillUnsatisfied(endsAt, bySeeding, at))
                {
                    unsatisfied++;
                }
                else if (previous is { } before && StillUnsatisfied(endsAt, bySeeding, before))
                {
                    _ = bySeeding ? satisfied++ : pastThePeriod++;
                }
            }

            rows.Add(new BurndownRow(at, unsatisfied, satisfied, pastThePeriod));
        }

        return rows;
    }

    /// <summary>
    /// When the torrent stops looking unsatisfied, and whether that is by being satisfied (true) or by its period ending
    /// (false). Satisfied takes the required time seeded; past the period starts after the deadline itself.
    /// </summary>
    private static (DateTimeOffset At, bool BySeeding) End(SeedingSnapshot torrent, SeedingRequirement requirement)
    {
        var deadline = torrent.SnatchedAt + requirement.Period;
        if (torrent.State == SeedingState.SeedingUnsatisfied)
        {
            var satisfiedAt = torrent.CheckedAt + (requirement.SeedTime - torrent.Seeded);
            if (satisfiedAt <= deadline)
            {
                return (satisfiedAt, true);
            }
        }

        return (deadline, false);
    }

    private static bool StillUnsatisfied(DateTimeOffset endsAt, bool bySeeding, DateTimeOffset at) =>
        bySeeding ? at < endsAt : at <= endsAt;
}
