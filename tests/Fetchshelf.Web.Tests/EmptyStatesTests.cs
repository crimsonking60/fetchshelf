namespace Fetchshelf.Web.Tests;

/// <summary>An empty list says why it is empty and what to do next.</summary>
public class EmptyStatesTests
{
    [Fact]
    public async Task Get_TorrentsWithoutAnyTorrent_PointsToTheLinkJobAndSearch()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.Contains("No torrents found.", html);
        Assert.Contains("Link job", html);
        Assert.DoesNotContain("empty-clear-filters", html);
    }

    [Fact]
    public async Task Get_TorrentsWithAFilterThatMatchesNothing_OffersToClearIt()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?Q=nothing-like-this");

        Assert.Contains("Nothing matches these filters.", html);
        Assert.Contains("id=\"empty-clear-filters\"", html);
    }

    [Fact]
    public async Task Get_EventsWithoutAnyEvent_SaysWhenOneIsRecorded()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("One is recorded when a torrent is grabbed, linked or removed from MAM.", html);
    }

    [Fact]
    public async Task Get_SeriesSearchThatMatchesNothing_OffersToClearTheSearch()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/series?Q=nothing-like-this");

        Assert.Contains("id=\"empty-clear-filters\"", html);
    }
}
