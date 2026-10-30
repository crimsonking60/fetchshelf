namespace Fetchshelf.Web.Tests;

/// <summary>Confirms the list-import jobs (one per `[[notion_list]]` entry) are registered (task 7.1).</summary>
public class ListImportJobRegistrationTests
{
    private const string ConfigToml = """
        mam_id = "test-mam-id"

        [[notion_list]]
        data_source = "abc123"
        token = "secret"
        name = "my-notion-list"
        mam_fields = ["MaM"]

        [[notion_list.grab]]
        cost = "free"

        [[notion_list]]
        data_source = "def456"
        token = "secret"
        name = "on-demand-list"
        mam_fields = ["MaM"]
        search_interval = 0

        [[notion_list.grab]]
        cost = "free"
        """;

    [Fact]
    public async Task HomePage_WithNotionListsConfigured_RegistersExpectedJobs()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();
        await client.GetAsync("/"); // forces the host to start, running startup registration

        Assert.NotNull(fixture.JobRegistry.Get("my-notion-list"));
        // The second entry has `search_interval = 0`: still registered (triggerable on demand), just not auto-scheduled.
        Assert.NotNull(fixture.JobRegistry.Get("on-demand-list"));
        Assert.Null(fixture.JobRegistry.Get("on-demand-list")!.NextScheduledRun);
    }
}
