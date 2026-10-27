using Fetchshelf.Core.Jobs;

namespace Fetchshelf.Core.Tests;

public class JobRegistryTests
{
    [Fact]
    public void Register_JobName_IsDiscoverableAfterward()
    {
        var registry = new JobRegistry();

        registry.Register("example-job", _ => Task.CompletedTask);

        Assert.NotNull(registry.Get("example-job"));
        Assert.Contains(registry.GetAll(), j => j.Name == "example-job");
    }

    [Fact]
    public void Register_WithDescription_IsStoredOnTheJob()
    {
        var registry = new JobRegistry();

        registry.Register("example-job", _ => Task.CompletedTask, "Does the example thing.");

        Assert.Equal("Does the example thing.", registry.Get("example-job")!.Description);
    }

    [Fact]
    public void SetNextScheduledRun_RegisteredJob_IsStoredOnTheJob()
    {
        var registry = new JobRegistry();
        registry.Register("example-job", _ => Task.CompletedTask);
        var nextRun = DateTimeOffset.UtcNow.AddMinutes(5);

        registry.SetNextScheduledRun("example-job", nextRun);

        Assert.Equal(nextRun, registry.Get("example-job")!.NextScheduledRun);
    }

    [Fact]
    public void SetNextScheduledRun_UnregisteredJob_IsIgnored()
    {
        var registry = new JobRegistry();

        registry.SetNextScheduledRun("missing-job", DateTimeOffset.UtcNow);

        Assert.Null(registry.Get("missing-job"));
    }

    [Fact]
    public void Register_WithoutDescription_IsNull()
    {
        var registry = new JobRegistry();

        registry.Register("example-job", _ => Task.CompletedTask);

        Assert.Null(registry.Get("example-job")!.Description);
    }

    [Fact]
    public async Task TriggerAsync_SuccessfulRun_RecordsStartTimeAndSuccess()
    {
        var registry = new JobRegistry();
        registry.Register("example-job", _ => Task.CompletedTask);
        var before = DateTimeOffset.UtcNow;

        await registry.TriggerAsync("example-job");

        var job = registry.Get("example-job")!;
        Assert.NotNull(job.LastRun);
        Assert.Equal(JobRunResult.Success, job.LastRun!.Result);
        Assert.True(job.LastRun.StartedAt >= before);
        Assert.Null(job.LastRun.ErrorMessage);
    }

    [Fact]
    public async Task TriggerAsync_RunFunctionThrows_RecordsFailureAndJobRemainsRunnable()
    {
        var registry = new JobRegistry();
        registry.Register("failing-job", _ => throw new InvalidOperationException("boom"));

        await registry.TriggerAsync("failing-job");

        var job = registry.Get("failing-job")!;
        Assert.Equal(JobRunResult.Failure, job.LastRun!.Result);
        Assert.Equal("boom", job.LastRun.ErrorMessage);

        // Job remains registered and runnable again after a failure.
        Assert.NotNull(registry.Get("failing-job"));
        await registry.TriggerAsync("failing-job");
        Assert.Equal(JobRunResult.Failure, registry.Get("failing-job")!.LastRun!.Result);
    }

    [Fact]
    public async Task TriggerAsync_OnDemand_RunsJobAndUpdatesLastRun()
    {
        var registry = new JobRegistry();
        var ran = false;
        registry.Register("on-demand-job", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await registry.TriggerAsync("on-demand-job");

        Assert.True(ran);
        Assert.NotNull(registry.Get("on-demand-job")!.LastRun);
    }

    [Fact]
    public async Task TriggerAsync_UnknownJobName_Throws()
    {
        var registry = new JobRegistry();

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.TriggerAsync("does-not-exist"));
    }

    [Fact]
    public async Task Get_ImmediatelyAfterTrigger_ReflectsUpdatedStatus()
    {
        var registry = new JobRegistry();
        registry.Register("example-job", _ => Task.CompletedTask);

        await registry.TriggerAsync("example-job");
        var statusImmediatelyAfter = registry.Get("example-job")!.LastRun;

        Assert.NotNull(statusImmediatelyAfter);
        Assert.Equal(JobRunResult.Success, statusImmediatelyAfter!.Result);
    }
}
