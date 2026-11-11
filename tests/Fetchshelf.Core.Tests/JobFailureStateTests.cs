using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Core.Tests;

public class JobFailureStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private static JobRunRecord Run(int hour, JobRunResult result, string? summary = null, JobFailureKind? kind = null) =>
        new(T0.AddHours(hour), result, summary, T0.AddHours(hour).AddMinutes(1), FailureKind: kind);

    [Fact]
    public void From_LatestRunsFailed_IsFailingSinceTheFirstFailureAfterTheLastSuccess() =>
        Assert.Equal(
            new JobFailure(T0.AddHours(2), 2, "403", JobFailureKind.MamRefused),
            JobFailureState.From([
                Run(0, JobRunResult.Failure, "old"),
                Run(1, JobRunResult.Success),
                Run(2, JobRunResult.Failure, "timeout"),
                Run(3, JobRunResult.Failure, "403", JobFailureKind.MamRefused),
            ]));

    [Theory]
    [InlineData(JobRunResult.Success)]
    [InlineData(JobRunResult.CompletedWithErrors)]
    [InlineData(JobRunResult.Cancelled)]
    public void From_LatestRunDidntFail_IsNotFailing(JobRunResult latest) =>
        Assert.Null(JobFailureState.From([Run(0, JobRunResult.Failure, "boom"), Run(1, latest)]));

    [Fact]
    public void From_NeverSucceeded_IsFailingSinceItsFirstRun() =>
        Assert.Equal(T0, JobFailureState.From([Run(0, JobRunResult.Failure, "a"), Run(1, JobRunResult.Failure, "b")])!.Since);

    [Fact]
    public void From_RunInterruptedBetweenFailures_NeitherEndsNorCountsInTheStreak() =>
        Assert.Equal(
            new JobFailure(T0, 2, "b", JobFailureKind.Other),
            JobFailureState.From([Run(0, JobRunResult.Failure, "a"), Run(1, JobRunResult.Interrupted), Run(2, JobRunResult.Failure, "b")]));

    [Fact]
    public void From_NoRuns_IsNotFailing() => Assert.Null(JobFailureState.From([]));
}

public class FailingJobGroupsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private static (string, JobFailure) Failing(string name, JobFailureKind kind, int hour = 0) =>
        (name, new JobFailure(T0.AddHours(hour), 1, "error", kind));

    [Fact]
    public void Group_SeveralJobsRefusedByMam_AreOneLineAndOtherFailuresKeepTheirOwn()
    {
        var groups = FailingJobGroups.Group([
            Failing("Autograb Freeleech", JobFailureKind.MamRefused, hour: 2),
            Failing("Link", JobFailureKind.Other),
            Failing("Snatchlist Unsat", JobFailureKind.MamRefused, hour: 1),
            Failing("Download", JobFailureKind.MamRefused, hour: 3),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].IsMamRefusal);
        Assert.Equal(["Autograb Freeleech", "Snatchlist Unsat", "Download"], groups[0].Jobs.Select(j => j.Name));
        Assert.Equal((JobFailureKind.MamRefused, T0.AddHours(1)), (groups[0].Kind, groups[0].Since));
        Assert.Equal("Link", Assert.Single(groups[1].Jobs).Name);
        Assert.False(groups[1].IsMamRefusal);
    }

    [Fact]
    public void Group_OneJobRefusedByMam_KeepsALineOfItsOwn() =>
        Assert.False(Assert.Single(FailingJobGroups.Group([Failing("Download", JobFailureKind.MamRefused)])).IsMamRefusal);

    [Fact]
    public void Group_RefusedAndBlocked_AreSeparateLines() =>
        Assert.Equal(
            [JobFailureKind.MamRefused, JobFailureKind.MamBlocked],
            FailingJobGroups.Group([
                Failing("A", JobFailureKind.MamRefused), Failing("B", JobFailureKind.MamBlocked),
                Failing("C", JobFailureKind.MamRefused), Failing("D", JobFailureKind.MamBlocked),
            ]).Select(g => g.Kind));
}
