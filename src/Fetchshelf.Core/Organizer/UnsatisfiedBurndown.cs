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

    /// <summary>Now (to the minute) and each of the next <see cref="Hours"/> hours.</summary>
    public static IReadOnlyList<DateTimeOffset> Times(DateTimeOffset now)
    {
        var start = ToTheMinute(now);
        return Enumerable.Range(0, Hours + 1).Select(hour => start.AddHours(hour)).ToList();
    }

    public static DateTimeOffset ToTheMinute(DateTimeOffset at) => new(at.Ticks - (at.Ticks % TimeSpan.TicksPerMinute), at.Offset);

    /// <summary>Rows for now (to the minute) and each of the next <see cref="Hours"/> hours.</summary>
    public static IReadOnlyList<BurndownRow> Build(IEnumerable<SeedingSnapshot> torrents, DateTimeOffset now, SeedingRequirement requirement) =>
        Build(torrents, Times(now), requirement);

    /// <summary>A row for each of <paramref name="times"/>, which are in order.</summary>
    public static IReadOnlyList<BurndownRow> Build(IEnumerable<SeedingSnapshot> torrents, IReadOnlyList<DateTimeOffset> times, SeedingRequirement requirement)
    {
        var ends = torrents.Where(t => t.State is SeedingState.SeedingUnsatisfied or SeedingState.NotSeedingUnsatisfied)
            .Select(t => End(t, requirement))
            .ToList();
        return Rows(ends, times, stillUnsatisfied => stillUnsatisfied);
    }

    /// <summary>
    /// Rows for MAM's own count: <paramref name="reported"/> torrents unsatisfied when MAM was read at
    /// <paramref name="reportedAt"/>, less those of <paramref name="torrents"/> satisfied, or past their period, since.
    /// A torrent already satisfied is taken to have been seeding until it was, so one satisfied after MAM was read
    /// counts too. Whatever MAM hadn't caught up with when it was read stays in the count, so the projection is never
    /// lower than MAM will be.
    /// </summary>
    /// <param name="torrents">Every MAM torrent in qBittorrent, satisfied ones included.</param>
    public static IReadOnlyList<BurndownRow> FromReported(
        long reported, DateTimeOffset reportedAt, IEnumerable<SeedingSnapshot> torrents, IReadOnlyList<DateTimeOffset> times, SeedingRequirement requirement)
    {
        var ends = torrents.Select(t => End(t, requirement))
            .Where(end => StillUnsatisfied(end.At, end.BySeeding, reportedAt))
            .ToList();
        return Rows(ends, times, stillUnsatisfied => (int)Math.Max(0, reported - (ends.Count - stillUnsatisfied)));
    }

    /// <param name="count">The row's count, from how many of <paramref name="ends"/> still look unsatisfied then.</param>
    private static List<BurndownRow> Rows(
        List<(DateTimeOffset At, bool BySeeding)> ends, IReadOnlyList<DateTimeOffset> times, Func<int, int> count)
    {
        var rows = new List<BurndownRow>(times.Count);
        DateTimeOffset? previous = null;
        foreach (var at in times)
        {
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

            rows.Add(new BurndownRow(at, count(unsatisfied), satisfied, pastThePeriod));
            previous = at;
        }

        return rows;
    }

    /// <summary>
    /// When the torrent stops looking unsatisfied, and whether that is by being satisfied (true) or by its period ending
    /// (false). Satisfied takes the required time seeded; past the period starts after the deadline itself. A satisfied
    /// torrent was satisfied when it had seeded the required time, counting back from when it was checked.
    /// </summary>
    private static (DateTimeOffset At, bool BySeeding) End(SeedingSnapshot torrent, SeedingRequirement requirement)
    {
        var deadline = torrent.SnatchedAt + requirement.Period;
        if (torrent.State is SeedingState.SeedingUnsatisfied or SeedingState.Satisfied)
        {
            var satisfiedAt = torrent.CheckedAt + (requirement.SeedTime - torrent.Seeded);
            if (satisfiedAt <= deadline || torrent.State == SeedingState.Satisfied)
            {
                return (satisfiedAt, true);
            }
        }

        return (deadline, false);
    }

    private static bool StillUnsatisfied(DateTimeOffset endsAt, bool bySeeding, DateTimeOffset at) =>
        bySeeding ? at < endsAt : at <= endsAt;
}
