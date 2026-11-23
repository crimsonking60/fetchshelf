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
}
