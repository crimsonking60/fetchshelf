namespace Fetchshelf.Web.Tests;

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

    [Fact]
    public async Task Get_MoreThanOnePage_OffersPageSizes_ThatKeepTheSearchAndStartAtPageOne()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 30);
        using var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/torrents?Q=Book&PageNumber=2"));

        var start = html.IndexOf("id=\"torrent-pager-size\"", StringComparison.Ordinal);
        var select = html[start..html.IndexOf("</select>", start, StringComparison.Ordinal)];
        var options = System.Text.RegularExpressions.Regex.Matches(select, @"<option value=""([^""]*)""( selected=""selected"")?>([^<]*)</option>");
        Assert.Equal(["25", "50", "100", "500", "All"], options.Select(o => o.Groups[3].Value));
        Assert.Equal("25", options.Single(o => o.Groups[2].Success).Groups[3].Value);
        var fifty = options.Single(o => o.Groups[3].Value == "50").Groups[1].Value;
        Assert.Contains("PageSize=50", fifty);
        Assert.Contains("Q=Book", fifty);
        Assert.DoesNotContain("PageNumber", fifty);
        Assert.Contains("PageSize=0", options.Single(o => o.Groups[3].Value == "All").Groups[1].Value);
    }

    [Fact]
    public async Task Get_PageSizeAll_ShowsEveryTorrent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 30);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?PageSize=0");

        Assert.Contains("Showing 30 of 30. Page 1 of 1.", html);
        Assert.Contains("Book 01", html);
        Assert.Contains("Book 30", html);
        Assert.Contains("selected=\"selected\">All</option>", html);
    }

    [Fact]
    public async Task Get_FewTorrents_HasNoPageSizes()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 3);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.DoesNotContain("id=\"torrent-pager-size\"", html);
    }

    [Fact]
    public async Task Get_ChosenPageSize_IsRememberedForTorrentsOnly()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 30);
        for (var i = 1; i <= 30; i++)
        {
            await OrganizerTestSupport.SeedEventAsync(fixture, new Fetchshelf.Data.Entities.EventEntity
            {
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(i),
                Kind = Fetchshelf.Data.Entities.EventKind.Linked,
                TorrentId = $"hash{i:D2}",
            });
        }

        using var client = fixture.CreateClient();

        await client.GetStringAsync("/torrents?PageSize=50");
        var torrents = await client.GetStringAsync("/torrents");
        var events = await client.GetStringAsync("/events");

        Assert.Contains("Showing 30 of 30. Page 1 of 1.", torrents);
        Assert.Contains("Showing 25 of 30. Page 1 of 2.", events);
    }

    [Fact]
    public async Task Get_PageSizeOutsideTheChoices_IsUsedButNotRemembered()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 30);
        using var client = fixture.CreateClient();

        var chosen = await client.GetStringAsync("/torrents?PageSize=3");
        var later = await client.GetStringAsync("/torrents");

        Assert.Contains("Page 1 of 10.", chosen);
        Assert.Contains("Showing 25 of 30. Page 1 of 2.", later);
    }

    [Fact]
    public async Task Get_RememberedPageSizeAll_ShowsEveryTorrent()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedAsync(fixture, 30);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/torrents?PageSize=0");
        var later = await client.GetStringAsync("/torrents");

        Assert.Contains("Showing 30 of 30. Page 1 of 1.", later);
    }
}
