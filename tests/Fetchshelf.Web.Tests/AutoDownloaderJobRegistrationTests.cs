namespace Fetchshelf.Web.Tests;

/// <summary>The auto-downloader's jobs (one per `[[autograb]]`/`[[snatchlist]]` entry, plus "Download") are registered.</summary>
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

    [Fact]
    public async Task HomePage_WithHardcoverListsConfigured_RegistersAJobForEach()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = "test-mam-id"

            [hardcover]
            token = "hc-token"

            [[hardcover_list]]
            name = "Want to Read"
            status = "want_to_read"

            [[hardcover_list.grab]]

            [[hardcover_list]]
            list = "books-to-fetch"
            search_interval = 0

            [[hardcover_list.grab]]
            """);
        var client = fixture.CreateClient();
        await client.GetAsync("/");

        Assert.NotNull(fixture.JobRegistry.Get("Want to Read"));
        Assert.NotNull(fixture.JobRegistry.Get("hardcover_list[1]"));
    }
}
