using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The sortable column headings of the Events, Series, Errors and Queue pages.</summary>
public class SortableHeadingsTests
{
    private static async Task SeedEventsAsync(FetchshelfWebAppFixture fixture)
    {
        // Oldest first as Grabbed, then Linked, so sorting by time and by type give different orders.
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity { OccurredAt = DateTimeOffset.UtcNow.AddHours(-3), Kind = EventKind.Grabbed, TorrentId = "hash-a", GrabberName = "A" });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity { OccurredAt = DateTimeOffset.UtcNow.AddHours(-2), Kind = EventKind.Linked, TorrentId = "hash-b", LinkerName = "Alpha lib" });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity { OccurredAt = DateTimeOffset.UtcNow.AddHours(-1), Kind = EventKind.Grabbed, TorrentId = "hash-c", GrabberName = "C" });
    }

    private static int Position(string html, string text) => html.IndexOf(text, StringComparison.Ordinal);

    [Fact]
    public async Task Events_DefaultsToNewestFirst_AndTheHeadingsOfferTheOtherOrders()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedEventsAsync(fixture);

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/events"));

        Assert.Matches(@"<th[^>]*aria-sort=""descending""[^>]*><a class=""sort"" href=""[^""]*Sort=OccurredAt[^""]*Dir=asc", html);
        Assert.Matches(@"<th[^>]*aria-sort=""none""[^>]*><a class=""sort"" href=""[^""]*Sort=Type[^""]*Dir=asc", html);
        Assert.True(Position(html, "hash-c") < Position(html, "hash-b") && Position(html, "hash-b") < Position(html, "hash-a"));
    }

    [Theory]
    [InlineData("OccurredAt", "asc", new[] { "hash-a", "hash-b", "hash-c" })]
    [InlineData("Type", "asc", new[] { "hash-c", "hash-a", "hash-b" })]
    [InlineData("Type", "desc", new[] { "hash-b", "hash-c", "hash-a" })]
    public async Task Events_SortByAHeading_OrdersTheRows_RowsOfOneTypeNewestFirst(string sort, string dir, string[] order)
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedEventsAsync(fixture);

        var html = await fixture.CreateClient().GetStringAsync($"/events?Sort={sort}&Dir={dir}");

        var positions = order.Select(id => Position(html, id)).ToList();
        Assert.True(positions.SequenceEqual(positions.Order()), $"Expected {string.Join(", ", order)}");
    }

    [Fact]
    public async Task Events_PageLinks_KeepTheSort()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedEventsAsync(fixture);

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/events?Sort=Type&Dir=desc&PageSize=1"));

        var next = Regex.Match(html, "<a id=\"pager-next\"[^>]*>").Value;
        Assert.Contains("Sort=Type", next);
        Assert.Contains("Dir=desc", next);
    }

    private static TorrentMetadata Book(string title, string series, string number) =>
        OrganizerTestSupport.BuildMetadata(title, ["An Author"]) with { Series = [SeriesParser.Parse(series, number)] };

    private static async Task SeedSeriesAsync(FetchshelfWebAppFixture fixture)
    {
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", Book("Alpha One", "Alpha", "1"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", Book("Zeta One", "Zeta", "1"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash3", Book("Zeta Two", "Zeta", "2"));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash4", Book("Zeta Three", "Zeta", "3"));
    }

    [Fact]
    public async Task Series_HeadingsSortByNameBooksAndFollowing_NameAscendingByDefault()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSeriesAsync(fixture);
        var client = fixture.CreateClient();

        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/series"));

        Assert.Matches(@"<th[^>]*aria-sort=""ascending""[^>]*><a class=""sort"" href=""[^""]*Sort=Name[^""]*Dir=desc", html);
        Assert.Matches(@"<th[^>]*aria-sort=""none""[^>]*><a class=""sort"" href=""[^""]*Sort=Books[^""]*Dir=desc", html);
        Assert.Matches(@"<th[^>]*aria-sort=""none""[^>]*><a class=""sort"" href=""[^""]*Sort=Following[^""]*Dir=asc", html);
        Assert.True(Position(html, ">Alpha</a>") < Position(html, ">Zeta</a>"));
    }

    [Theory]
    [InlineData("Name", "desc", false)]
    [InlineData("Books", "desc", false)]
    [InlineData("Books", "asc", true)]
    public async Task Series_SortByAHeading_OrdersTheRows(string sort, string dir, bool alphaFirst)
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSeriesAsync(fixture);

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync($"/series?Sort={sort}&Dir={dir}"));

        Assert.Equal(alphaFirst, Position(html, ">Alpha</a>") < Position(html, ">Zeta</a>"));
    }

    [Fact]
    public async Task Errors_HeadingsSortByJobAndLastFailed()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Fetchshelf.Data.FetchshelfDbContext>();
            db.ErroredTorrents.Add(new ErroredTorrentEntity { Stage = ErrorStage.Linker, Key = "hash1", ErrorText = "boom", OccurredAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/errors"));

        Assert.Matches(@"<th[^>]*aria-sort=""ascending""[^>]*><a class=""sort"" href=""[^""]*Sort=Job[^""]*Dir=desc", html);
        Assert.Matches(@"<th[^>]*aria-sort=""none""[^>]*><a class=""sort"" href=""[^""]*Sort=LastFailed[^""]*Dir=desc", html);
    }
}
