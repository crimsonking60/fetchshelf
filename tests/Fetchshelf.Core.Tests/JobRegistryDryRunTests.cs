using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Core.Tests;

/// <summary>A job's dry run (<see cref="JobTrigger.DryRun"/>), and the details a run's outcome lists.</summary>
public class JobRegistryDryRunTests
{
    private sealed class Recorder : IJobRunListener
    {
        public List<JobRunFinished> Runs { get; } = [];

        public void RunFinished(JobRunFinished finished) => Runs.Add(finished);
    }

    private sealed class RestoredStore(string jobName, params JobRunRecord[] runs) : IJobRunStore
    {
        public Task<long> StartedAsync(string name, DateTimeOffset startedAt, JobTrigger trigger) => Task.FromResult(1L);

        public Task FinishedAsync(long runId, JobRunRecord run) => Task.CompletedTask;

        public Task InterruptUnfinishedAsync(string summary) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, IReadOnlyList<JobRunRecord>>> RecentRunsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<JobRunRecord>>>(
                new Dictionary<string, IReadOnlyList<JobRunRecord>> { [jobName] = runs });
    }

    [Fact]
    public async Task TriggerAsync_DryRun_RunsTheDryRunDelegate_AndKeepsItsDetails()
    {
        var registry = new JobRegistry();
        var (real, dry) = (0, 0);
        registry.Register(
            "Autograb Bookmarks",
            _ => { real++; return Task.FromResult<JobOutcome?>(new JobOutcome("1 selected")); },
            dryRunAsync: _ => { dry++; return Task.FromResult<JobOutcome?>(new JobOutcome("Dry run: 1 would have been selected", Details: ["Would select: A - MAM 1"])); });

        await registry.TriggerAsync("Autograb Bookmarks", JobTrigger.DryRun);

        Assert.Equal((0, 1), (real, dry));
        var run = registry.Get("Autograb Bookmarks")!.LastRun!;
        Assert.Equal(JobTrigger.DryRun, run.Trigger);
        Assert.Equal(["Would select: A - MAM 1"], run.Details);
    }

    [Fact]
    public async Task TriggerAsync_DryRunOfAJobWithoutOne_IsRefused()
    {
        var registry = new JobRegistry();
        registry.Register("Link", _ => Task.FromResult<JobOutcome?>(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.TriggerAsync("Link", JobTrigger.DryRun));
        Assert.Null(registry.Get("Link")!.LastRun);
    }

    [Fact]
    public async Task TriggerAsync_FailingDryRun_StartsNoFailingStreak_AndTellsNoListener()
    {
        var recorder = new Recorder();
        var registry = new JobRegistry(listeners: [recorder]);
        registry.Register("Upgrade", _ => Task.FromResult<JobOutcome?>(null), dryRunAsync: _ => throw new InvalidOperationException("MAM said no"));

        await registry.TriggerAsync("Upgrade", JobTrigger.DryRun);

        var job = registry.Get("Upgrade")!;
        Assert.Equal(JobRunResult.Failure, job.LastRun!.Result);
        Assert.Null(job.Failing);
        Assert.Empty(recorder.Runs);
    }

    [Fact]
    public async Task RestoredDryRunFailures_StartNoFailingStreak()
    {
        var at = new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);
        var registry = new JobRegistry(store: new RestoredStore(
            "Upgrade",
            new JobRunRecord(at, JobRunResult.Failure, "MAM said no", at, JobTrigger.DryRun),
            new JobRunRecord(at.AddHours(1), JobRunResult.Failure, "MAM said no", at.AddHours(1), JobTrigger.DryRun)));
        await registry.LoadLastRunsAsync();

        registry.Register("Upgrade", _ => Task.FromResult<JobOutcome?>(null));

        Assert.Null(registry.Get("Upgrade")!.Failing);
    }

    [Fact]
    public async Task TriggerAsync_RunWithErrors_KeepsItsDetails()
    {
        var registry = new JobRegistry();
        registry.Register("Hardcover", _ => throw new JobCompletedWithErrorsException("1 selected; 1 failed", details: ["Selected: B - MAM 2"]));

        await registry.TriggerAsync("Hardcover");

        Assert.Equal(["Selected: B - MAM 2"], registry.Get("Hardcover")!.LastRun!.Details);
    }
}
