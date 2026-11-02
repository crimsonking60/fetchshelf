using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Tests;

/// <summary>The Events page's page navigation.</summary>
public class EventsPagingTests
{
    private static async Task SeedAsync(FetchshelfWebAppFixture fixture, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
            {
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(i),
                Kind = EventKind.Linked,
                TorrentId = $"hash{i:D2}",
                LinkerName = $"Linker {i:D2}",
            });
        }
    }

    [Fact]
    public async Task Get_FirstPage_OffersNextAndLastButNotFirstOrPrevious()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events?PageSize=3&Type=Linked");

        Assert.Contains("Showing 3 of 7. Page 1 of 3.", html);
        Assert.Contains("id=\"pager-next\"", html);
        Assert.Contains("id=\"pager-last\"", html);
        Assert.DoesNotContain("id=\"pager-prev\"", html);
        Assert.DoesNotContain("id=\"pager-first\"", html);
        Assert.Contains("PageNumber=3", html);
        Assert.Contains("PageNumber=2", html);
        Assert.Contains("Type=Linked", html);
    }

    [Fact]
    public async Task Get_FirstPage_ShowsTheNewestEvents()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events?PageSize=3");

        Assert.Contains("Linker 07", html);
        Assert.Contains("Linker 05", html);
        Assert.DoesNotContain("Linker 04", html);
    }

    [Fact]
    public async Task Get_LastPage_ShowsRemainderAndOffersFirstAndPreviousOnly()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events?PageSize=3&PageNumber=3");

        Assert.Contains("Showing 1 of 7. Page 3 of 3.", html);
        Assert.Contains("Linker 01", html);
        Assert.Contains("id=\"pager-prev\"", html);
        Assert.Contains("id=\"pager-first\"", html);
        Assert.Contains("PageNumber=1", html);
        Assert.DoesNotContain("id=\"pager-next\"", html);
        Assert.DoesNotContain("id=\"pager-last\"", html);
    }

    [Fact]
    public async Task Get_PageBeyondTheEnd_ShowsTheLastPage()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 7);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events?PageSize=3&PageNumber=99");

        Assert.Contains("Page 3 of 3", html);
    }

    [Fact]
    public async Task Get_SinglePage_HasNoPager()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 2);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("Showing 2 of 2. Page 1 of 1.", html);
        Assert.DoesNotContain("id=\"event-pager\"", html);
    }
}
