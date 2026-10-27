namespace Fetchshelf.Web.Tests;

/// <summary>Confirms the auto-downloader change's jobs (one per `[[autograb]]`/`[[snatchlist]]` entry, plus "Download") are registered (task 9.1).</summary>
public class AutoDownloaderJobRegistrationTests
{
    private const string ConfigToml = """
        mam_id = "test-mam-id"

        [[autograb]]
        type = "bookmarks"
        name = "my-autograb"

        [[autograb]]
        type = "new"
        search_interval = 0

        [[snatchlist]]
        type = "unsat"
        name = "my-snatchlist"
        """;

    [Fact]
    public async Task HomePage_WithAutograbAndSnatchlistConfigured_RegistersExpectedJobs()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var client = fixture.CreateClient();
        await client.GetAsync("/"); // forces the host to start, running startup registration

        Assert.NotNull(fixture.JobRegistry.Get("my-autograb"));
        // The second entry is unnamed and has `search_interval = 0` - still registered (triggerable
        // on demand), just not auto-scheduled, under the synthesized name `autograb[1]`.
        Assert.NotNull(fixture.JobRegistry.Get("autograb[1]"));
        Assert.NotNull(fixture.JobRegistry.Get("my-snatchlist"));
        Assert.NotNull(fixture.JobRegistry.Get("Download"));
    }
}
