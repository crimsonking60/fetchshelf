using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Tests;

public class StatisticsPageTests
{
    [Fact]
    public async Task StatisticsPage_EmptyDatabase_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await fixture.CreateClient().GetStringAsync("/Statistics");

        Assert.Contains("<h2>Statistics</h2>", html);
        Assert.Contains("id=\"no-books-message\"", html);
        Assert.Contains("id=\"no-job-runs-message\"", html);
        Assert.Contains("id=\"grabs-per-month\"", html);
        Assert.Matches("<a [^>]*aria-current=\"page\"[^>]*>Statistics</a>", html);
    }

    [Fact]
    public async Task StatisticsPage_ShowsTheLibraryAndTheGrabs()
    {
        using var fixture = new FetchshelfWebAppFixture("""
            mam_id = ""

            [[autograb]]
            name = "New books"
            type = "new"
            """);
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: "/library/book");
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            TorrentId = "hash1",
            MamId = 1,
            Kind = EventKind.Grabbed,
            Cost = "Ratio",
            GrabberName = "New books",
        });

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Statistics"));

        Assert.Contains("<p id=\"library-total\">1 book, ", html);
        Assert.Contains("<td>Audiobook</td><td class=\"num\">1</td>", html);
        Assert.Contains("<td>New books</td><td>Autograb</td><td class=\"num\">1</td>", html);
    }
}
