namespace Fetchshelf.Core.AutoDownloader.Tests;

public sealed class ListSearchScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NeverSearched_IsDueNow()
    {
        Assert.Null(ListSearchSchedule.NextSearch(lastSearched: null, missingSince: null, Now));
        Assert.True(ListSearchSchedule.IsDue(null, null, Now));
    }

    [Fact]
    public void FoundSomething_IsSearchedAgainADayLater()
    {
        var last = Now.AddHours(-23);

        Assert.Equal(last.AddDays(1), ListSearchSchedule.NextSearch(last, missingSince: null, Now));
        Assert.False(ListSearchSchedule.IsDue(last, null, Now));
        Assert.True(ListSearchSchedule.IsDue(Now.AddDays(-1), null, Now));
    }

    [Fact]
    public void MissingForLessThanAWeek_IsStillSearchedDaily()
    {
        Assert.True(ListSearchSchedule.IsDue(Now.AddDays(-1), missingSince: Now.AddDays(-6), Now));
    }

    [Fact]
    public void MissingForAWeek_IsSearchedWeekly()
    {
        var missingSince = Now.AddDays(-8);

        Assert.False(ListSearchSchedule.IsDue(Now.AddDays(-2), missingSince, Now));
        Assert.Equal(Now.AddDays(-2).AddDays(7), ListSearchSchedule.NextSearch(Now.AddDays(-2), missingSince, Now));
        Assert.True(ListSearchSchedule.IsDue(Now.AddDays(-7), missingSince, Now));
    }
}
