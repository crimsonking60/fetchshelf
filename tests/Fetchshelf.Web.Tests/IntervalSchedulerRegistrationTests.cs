namespace Fetchshelf.Web.Tests;

/// <summary>Confirms every timed job gets its own interval scheduler, not just the first one registered.</summary>
public class IntervalSchedulerRegistrationTests
{
    private const string ConfigToml = """
        mam_id = "test-mam-id"
        link_interval = 10
        search_interval = 15

        [[goodreads_list]]
        url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
        name = "my-goodreads-list"
        search_interval = 20

        [[goodreads_list.grab]]
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
        Assert.NotNull(fixture.JobRegistry.Get("my-goodreads-list")!.NextScheduledRun);
    }
}
