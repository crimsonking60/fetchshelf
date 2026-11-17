using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public sealed class SeedingEstimatorTests
{
    private static readonly SeedingRequirement Requirement = new(72, 30);
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static SeedingEstimate Estimate(double seededHours, double daysAgo, bool seeding, SeedingState? previous = null) =>
        SeedingEstimator.Estimate(TimeSpan.FromHours(seededHours), Now.AddDays(-daysAgo), seeding, previous, Now, Requirement);

    [Theory]
    [InlineData(72, true)]
    [InlineData(500, false)]
    public void Estimate_SeededTheRequiredTime_IsSatisfied_SeedingOrNot(double hours, bool seeding) =>
        Assert.Equal(SeedingState.Satisfied, Estimate(hours, 5, seeding).State);

    [Fact]
    public void Estimate_SeedingButShort_SaysHowMuchMoreAndByWhen()
    {
        var estimate = Estimate(41, 5, seeding: true);

        Assert.Equal(SeedingState.SeedingUnsatisfied, estimate.State);
        Assert.True(estimate.LooksUnsatisfied);
        Assert.Equal(TimeSpan.FromHours(31), estimate.StillNeeded);
        Assert.Equal(Now.AddDays(25), estimate.Deadline);
        Assert.Equal("Seeded 41 of 72 hours; needs 31 more by 2026-12-12.", SeedingEstimator.Describe(estimate, Requirement));
    }

    [Fact]
    public void Estimate_NotSeedingAndShort_IsNotSeedingUnsatisfied() =>
        Assert.Equal(SeedingState.NotSeedingUnsatisfied, Estimate(10, 5, seeding: false).State);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Estimate_PeriodOverWithoutEnoughSeeding_IsPastThePeriod(bool seeding)
    {
        var estimate = Estimate(10, 31, seeding);

        Assert.Equal(SeedingState.PastThePeriod, estimate.State);
        Assert.False(estimate.LooksUnsatisfied);
    }

    [Fact]
    public void Estimate_OnceSatisfied_StaysSatisfied_EvenWithLessSeedingTime() =>
        Assert.Equal(SeedingState.Satisfied, Estimate(0, 60, seeding: false, previous: SeedingState.Satisfied).State);

    [Theory]
    [InlineData("uploading", true)]
    [InlineData("stalledUP", true)]
    [InlineData("forcedUP", true)]
    [InlineData("stoppedUP", false)]
    [InlineData("pausedUP", false)]
    [InlineData("queuedUP", false)]
    [InlineData("error", false)]
    public void IsSeeding_OnlyTheSeedingStates(string state, bool expected) => Assert.Equal(expected, SeedingEstimator.IsSeeding(state));

    [Fact]
    public void Describe_RoundsSeededDownAndNeededUp() =>
        Assert.Equal(
            "Not seeding, and seeded 40 of 72 hours: seed it 32 more by 2026-12-12 to avoid a hit-and-run.",
            SeedingEstimator.Describe(Estimate(40.5, 5, seeding: false), Requirement));
}
