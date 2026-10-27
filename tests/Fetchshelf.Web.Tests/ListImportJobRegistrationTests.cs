namespace Fetchshelf.Web.Tests;

/// <summary>Confirms the list-import change's jobs (one per `[[goodreads_list]]`/`[[notion_list]]` entry) are registered (task 7.1).</summary>
public class ListImportJobRegistrationTests
{
    private const string ConfigToml = """
        mam_id = "test-mam-id"

        [[goodreads_list]]
        url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
        name = "my-goodreads-list"

        [[goodreads_list.grab]]
        cost = "free"

        [[goodreads_list]]
        url = "https://www.goodreads.com/review/list_rss/2?shelf=read"
        search_interval = 0

        [[goodreads_list.grab]]
        cost = "free"

        [[notion_list]]
        data_source = "abc123"
        token = "secret"
        name = "my-notion-list"
        mam_fields = ["MaM"]

        [[notion_list.grab]]
        cost = "free"
        """;

    [Fact]
    public async Task HomePage_WithGoodreadsAndNotionListsConfigured_RegistersExpectedJobs()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();
        await client.GetAsync("/"); // forces the host to start, running startup registration

        Assert.NotNull(fixture.JobRegistry.Get("my-goodreads-list"));
        // The second entry is unnamed and has `search_interval = 0` - still registered (triggerable
        // on demand), just not auto-scheduled, under the synthesized name `goodreads_list[1]`.
        Assert.NotNull(fixture.JobRegistry.Get("goodreads_list[1]"));
        Assert.NotNull(fixture.JobRegistry.Get("my-notion-list"));
    }
}
