namespace Fetchshelf.Web.Tests;

/// <summary>Confirms every timed job gets its own interval scheduler, not just the first one registered.</summary>
public class IntervalSchedulerRegistrationTests
{
    private const string ConfigToml = """
        mam_id = "test-mam-id"
        link_interval = 10
        search_interval = 15

        [[notion_list]]
        data_source = "abc123"
        token = "secret"
        name = "my-notion-list"
        mam_fields = ["MaM"]
        search_interval = 20

        [[notion_list.grab]]
        cost = "free"
        """;

    [Fact]
    public async Task Startup_SeveralTimedJobsConfigured_EachHasANextScheduledRun()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();
        await client.GetAsync("/"); // forces the host to start, which starts the hosted schedulers

        Assert.NotNull(fixture.JobRegistry.Get("Link")!.NextScheduledRun);
        Assert.NotNull(fixture.JobRegistry.Get("Download")!.NextScheduledRun);
        Assert.NotNull(fixture.JobRegistry.Get("my-notion-list")!.NextScheduledRun);
    }
}
