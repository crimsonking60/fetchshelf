namespace Fetchshelf.Web.Tests;

public class BulkBarTests
{
    [Fact]
    public async Task TorrentsPage_HasTheBulkBarWithItsButtons_AndLoadsTheScriptThatShowsIt()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

        var html = await fixture.CreateClient().GetStringAsync("/torrents");

        Assert.Contains("<script src=\"/js/bulk-bar.", html);
        var bar = html[html.IndexOf("<div class=\"bulk-bar\" data-bulk-bar>", StringComparison.Ordinal)..];
        Assert.Contains("data-bulk-count", bar);
        foreach (var action in new[] { "clean", "refresh", "relink", "unlink", "removeRecord" })
        {
            Assert.Contains($"name=\"action\" value=\"{action}\"", bar);
        }
    }
}
