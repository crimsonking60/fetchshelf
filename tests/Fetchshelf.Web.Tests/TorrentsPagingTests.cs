namespace Fetchshelf.Web.Tests;

/// <summary>The Torrents page's page navigation.</summary>
public class TorrentsPagingTests
{
    private static async Task SeedAsync(FetchshelfWebAppFixture fixture, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await OrganizerTestSupport.SeedTorrentAsync(fixture, $"hash{i:D2}", OrganizerTestSupport.BuildMetadata($"Book {i:D2}"));
        }
    }

    [Fact]
    public async Task Get_FirstPage_OffersNextAndLastButNotFirstOrPrevious()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?PageSize=3&Q=Book");

        Assert.Contains("Page 1 of 3", html);
        Assert.Contains("id=\"pager-next\"", html);
        Assert.Contains("id=\"pager-last\"", html);
        Assert.DoesNotContain("id=\"pager-prev\"", html);
        Assert.DoesNotContain("id=\"pager-first\"", html);
        Assert.Contains("PageNumber=3", html);
        Assert.Contains("PageNumber=2", html);
        Assert.Contains("Q=Book", html);
    }

    [Fact]
    public async Task Get_LastPage_ShowsRemainderAndOffersFirstAndPreviousOnly()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?PageSize=3&PageNumber=3");

        Assert.Contains("Showing 1 of 7. Page 3 of 3.", html);
        Assert.Contains("id=\"pager-prev\"", html);
        Assert.Contains("id=\"pager-first\"", html);
        Assert.Contains("PageNumber=1", html);
        Assert.DoesNotContain("id=\"pager-next\"", html);
        Assert.DoesNotContain("id=\"pager-last\"", html);
    }

    [Fact]
    public async Task Get_MiddlePage_OffersAllFour()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?PageSize=3&PageNumber=2");

        foreach (var id in new[] { "pager-first", "pager-prev", "pager-next", "pager-last" })
        {
            Assert.Contains($"id=\"{id}\"", html);
        }
    }

    [Fact]
    public async Task Get_PageBeyondTheEnd_ShowsTheLastPage()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?PageSize=3&PageNumber=99");

        Assert.Contains("Page 3 of 3", html);
    }

    [Fact]
    public async Task Get_SinglePage_HasNoPager()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 2);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.DoesNotContain("id=\"torrent-pager\"", html);
    }
}
