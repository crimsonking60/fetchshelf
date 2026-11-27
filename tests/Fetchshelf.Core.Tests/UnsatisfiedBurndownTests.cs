using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public sealed class UnsatisfiedBurndownTests
{
    private static readonly SeedingRequirement Requirement = new(72, 30);
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 34, 0, TimeSpan.Zero);

    private static SeedingSnapshot Torrent(SeedingState state, double seededHours, double daysAgo = 5, double checkedHoursAgo = 0) =>
        new(state, TimeSpan.FromHours(seededHours), Now.AddDays(-daysAgo), Now.AddHours(-checkedHoursAgo));

    private static IReadOnlyList<BurndownRow> Build(params SeedingSnapshot[] torrents) => UnsatisfiedBurndown.Build(torrents, Now, Requirement);

    [Fact]
    public void Build_HasNowToTheMinuteAndEachOfTheNext72Hours()
    {
        var rows = UnsatisfiedBurndown.Build([], Now.AddSeconds(56), Requirement);

        Assert.Equal(73, rows.Count);
        Assert.Equal(Now, rows[0].At);
        Assert.Equal(rows[0].At.AddHours(72), rows[^1].At);
        Assert.All(rows, row => Assert.Equal(0, row.Unsatisfied));
    }

    [Fact]
    public void Build_ATorrentSeedingNow_IsSatisfiedOnceItHasSeededWhatItStillNeeds()
    {
        // 41 of 72 hours: 31 to go, so it still counts 30 hours on and no longer 31 hours on; 10 hours: 62 to go.
        var rows = Build(Torrent(SeedingState.SeedingUnsatisfied, 41), Torrent(SeedingState.SeedingUnsatisfied, 10));

        Assert.Equal(2, rows[0].Unsatisfied);
        Assert.Equal(2, rows[30].Unsatisfied);
        Assert.Equal(1, rows[31].Unsatisfied);
        Assert.True(rows[31].Fewer);
        Assert.Equal(1, rows[31].Satisfied);
        Assert.False(rows[32].Fewer);
        Assert.Equal(1, rows[61].Unsatisfied);
        Assert.Equal(0, rows[62].Unsatisfied);
        Assert.Equal(0, rows[72].Unsatisfied);
    }

    [Fact]
    public void Build_TheTimeSinceTheLastEstimate_CountsAsSeeded()
    {
        var rows = Build(Torrent(SeedingState.SeedingUnsatisfied, 41, checkedHoursAgo: 10));

        Assert.Equal(1, rows[20].Unsatisfied);
        Assert.Equal(0, rows[21].Unsatisfied);
    }

    [Fact]
    public void Build_ATorrentNotSeeding_CountsUntilItsPeriodEnds_ThenIsPastThePeriod()
    {
        // Snatched 29 days and 12 hours ago: the period ends exactly 12 hours from now, and
        // a torrent counts on its deadline itself.
        var rows = Build(Torrent(SeedingState.NotSeedingUnsatisfied, 10, daysAgo: 29.5));

        Assert.Equal(1, rows[12].Unsatisfied);
        Assert.Equal(0, rows[13].Unsatisfied);
        Assert.Equal(1, rows[13].PastThePeriod);
        Assert.Equal(0, rows[13].Satisfied);
        Assert.True(rows[13].Fewer);
    }

    [Fact]
    public void Build_ATorrentSeedingButTooLateForThePeriod_DropsOutAtItsDeadline()
    {
        var rows = Build(Torrent(SeedingState.SeedingUnsatisfied, 10, daysAgo: 29.5));

        Assert.Equal(0, rows[13].Unsatisfied);
        Assert.Equal(1, rows[13].PastThePeriod);
    }

    [Theory]
    [InlineData(SeedingState.Satisfied)]
    [InlineData(SeedingState.PastThePeriod)]
    public void Build_TorrentsNotUnsatisfiedNow_AreNotCounted(SeedingState state) =>
        Assert.All(Build(Torrent(state, 10)), row => Assert.Equal(0, row.Unsatisfied));

    [Fact]
    public void Build_AtTheGivenTimes_StartsWithTheEstimateBeforeNow()
    {
        // Estimated 30 minutes ago with 20 minutes to go: counted then, satisfied by now.
        var torrent = Torrent(SeedingState.SeedingUnsatisfied, 72 - (20 / 60.0), checkedHoursAgo: 0.5);

        var rows = UnsatisfiedBurndown.Build([torrent], [Now.AddMinutes(-30), .. UnsatisfiedBurndown.Times(Now)], Requirement);

        Assert.Equal(74, rows.Count);
        Assert.Equal((Now.AddMinutes(-30), 1), (rows[0].At, rows[0].Unsatisfied));
        Assert.Equal(0, rows[1].Unsatisfied);
        Assert.Equal(1, rows[1].Satisfied);
    }

    [Fact]
    public void FromReported_StartsFromMamsCount_LessWhatIsSatisfiedAfterItWasRead()
    {
        var readAt = Now.AddMinutes(-30);
        var torrents = new[]
        {
            // Satisfied 10 minutes ago, after MAM was read: off the count now.
            Torrent(SeedingState.Satisfied, 72 + (10 / 60.0)),
            // Satisfied an hour ago, before MAM was read: MAM's count already has it, or will, so it isn't taken off.
            Torrent(SeedingState.Satisfied, 73),
            // Satisfied 5 hours from now.
            Torrent(SeedingState.SeedingUnsatisfied, 67),
            // Not seeding: counts until its period ends.
            Torrent(SeedingState.NotSeedingUnsatisfied, 10),
        };

        var rows = UnsatisfiedBurndown.FromReported(20, readAt, torrents, UnsatisfiedBurndown.Times(Now), Requirement);

        Assert.Equal(19, rows[0].Unsatisfied);
        Assert.Equal(19, rows[4].Unsatisfied);
        Assert.Equal(18, rows[5].Unsatisfied);
        Assert.Equal(1, rows[5].Satisfied);
        Assert.Equal(18, rows[72].Unsatisfied);
    }

    [Fact]
    public void FromReported_NeverGoesBelowZero()
    {
        var rows = UnsatisfiedBurndown.FromReported(
            0, Now, [Torrent(SeedingState.SeedingUnsatisfied, 71)], UnsatisfiedBurndown.Times(Now), Requirement);

        Assert.All(rows, row => Assert.Equal(0, row.Unsatisfied));
    }
}
