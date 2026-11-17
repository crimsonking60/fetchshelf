using System.Net;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public class ListsPagesTests
{
    private const string ListId = "hardcover:status:want_to_read";

    private const string ConfigToml = """
        mam_id = ""

        [hardcover]
        token = "hc-token"

        [[hardcover_list]]
        name = "Wishlist"
        status = "want_to_read"

        [[hardcover_list.grab]]
        """;

    private static async Task SeedAsync(FetchshelfWebAppFixture fixture, params ListItemEntity[] items)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        foreach (var listId in items.Select(i => i.ListId).Append(ListId).Distinct())
        {
            dbContext.Lists.Add(new ListEntity
            {
                Id = listId,
                Name = listId == ListId ? "Wishlist" : "Old list",
                ListType = "Hardcover",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        dbContext.ListItems.AddRange(items);
        await dbContext.SaveChangesAsync();
    }

    private static ListItemEntity Item(string externalId, string title, int position = 0, string listId = ListId) => new()
    {
        ListId = listId,
        ExternalId = externalId,
        Position = position,
        BookTitle = title,
        AuthorsJson = "[\"Frank Herbert\"]",
        AllowAudio = true,
        AllowEbook = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<ListItemEntity> ReadItemAsync(FetchshelfWebAppFixture fixture, string externalId)
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().ListItems.AsNoTracking().SingleAsync(i => i.ExternalId == externalId);
    }

    [Fact]
    public async Task Menu_ShowsListsOnlyWhenAHardcoverListIsConfigured()
    {
        // One host at a time: a fixture hands its config to the host when the host starts.
        string withHtml;
        using (var with = new FetchshelfWebAppFixture(ConfigToml))
        {
            withHtml = await with.CreateClient().GetStringAsync("/");
        }

        using var without = new FetchshelfWebAppFixture();
        var withoutHtml = await without.CreateClient().GetStringAsync("/");

        Assert.Contains(">Lists</a>", withHtml);
        Assert.DoesNotContain(">Lists</a>", withoutHtml);
    }

    [Fact]
    public async Task Index_CountsBooksAndThoseMissingAFormat_AndListsOldListsSeparately()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var complete = Item("1", "Dune");
        complete.AudioStatus = ListItemSideStatus.Selected;
        complete.EbookStatus = ListItemSideStatus.Existing;
        var removed = Item("3", "Gone");
        removed.RemovedAt = DateTimeOffset.UtcNow;
        await SeedAsync(fixture, complete, Item("2", "Dune Messiah"), removed, Item("9", "Old Book", listId: "hardcover:list:old"));

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/Lists"));

        var configured = html[html.IndexOf("id=\"configured-lists\"", StringComparison.Ordinal)..html.IndexOf("id=\"inactive-lists\"", StringComparison.Ordinal)];
        Assert.Matches(@"Wishlist</a></td>\s*<td>Hardcover</td>\s*<td>2</td>\s*<td>1</td>", configured);
        Assert.Contains("Old list", html[html.IndexOf("id=\"inactive-lists\"", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task Detail_ShowsBooksInTheListsOrder_WithWhatWasFound()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var first = Item("2", "Dune Messiah", position: 1);
        first.AudioStatus = ListItemSideStatus.NotFound;
        first.EbookStatus = ListItemSideStatus.Wanted;
        var second = Item("1", "Dune", position: 0);
        second.AudioStatus = ListItemSideStatus.Existing;
        second.AudioMamId = 5;
        await SeedAsync(fixture, first, second);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash5", OrganizerTestSupport.BuildMetadata("Dune") with { MamId = 5 });
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            (await dbContext.Torrents.SingleAsync()).MamId = 5;
            await dbContext.SaveChangesAsync();
        }

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync($"/Lists/Detail/{Uri.EscapeDataString(ListId)}"));

        Assert.True(html.IndexOf(">\n                    Dune\n", StringComparison.Ordinal) < html.IndexOf("Dune Messiah", StringComparison.Ordinal));
        Assert.Contains("<a href=\"/torrents/hash5\">In the library</a>", html);
        Assert.Contains("Not found on MAM", html);
        Assert.Contains("costs more than the list's grab blocks allow", html);
        Assert.Contains("Not searched for yet", html);
    }

    [Fact]
    public async Task Detail_Filters_HideFoundAndRemovedBooks()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var found = Item("1", "Found Book");
        found.AudioStatus = ListItemSideStatus.Selected;
        found.EbookStatus = ListItemSideStatus.NotFound;
        var removed = Item("2", "Removed Book");
        removed.RemovedAt = DateTimeOffset.UtcNow;
        await SeedAsync(fixture, found, removed, Item("3", "Waiting Book"));
        var client = fixture.CreateClient();
        var url = $"/Lists/Detail/{Uri.EscapeDataString(ListId)}";

        var audioMissing = await client.GetStringAsync($"{url}?Show=audio");
        var withRemoved = await client.GetStringAsync($"{url}?Removed=true");

        Assert.DoesNotContain("Found Book", audioMissing);
        Assert.DoesNotContain("Removed Book", audioMissing);
        Assert.Contains("Waiting Book", audioMissing);
        Assert.Contains("Removed Book", withRemoved);
        Assert.Contains("(no longer on the list)", withRemoved);
    }

    [Fact]
    public async Task Detail_ConfiguredListNotReadYet_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);

        var response = await fixture.CreateClient().GetAsync($"/Lists/Detail/{Uri.EscapeDataString(ListId)}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Wishlist", html);
        Assert.Contains("has not read this list from Hardcover yet", html);
    }

    [Fact]
    public async Task Detail_UnknownList_IsNotFound()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);

        var response = await fixture.CreateClient().GetAsync("/Lists/Detail/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkDone_AndUndo_KeepTheFilter()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        await SeedAsync(fixture, Item("1", "Dune"));
        var client = fixture.CreateNoRedirectClient();
        var url = $"/Lists/Detail/{Uri.EscapeDataString(ListId)}";
        var html = await client.GetStringAsync($"{url}?Show=any");
        var token = System.Text.RegularExpressions.Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        var form = new Dictionary<string, string> { ["externalId"] = "1", ["__RequestVerificationToken"] = token };

        using var done = await client.PostAsync($"{url}?handler=MarkDone&Show=any", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Contains("Show=any", done.Headers.Location!.ToString());
        Assert.NotNull((await ReadItemAsync(fixture, "1")).MarkedDoneAt);

        using var undone = await client.PostAsync($"{url}?handler=UndoDone&Show=any", new FormUrlEncodedContent(form));
        Assert.Null((await ReadItemAsync(fixture, "1")).MarkedDoneAt);
    }

    [Fact]
    public async Task Detail_BookMissingFromMam_SaysSince_WhenItIsNextSearched_AndOffersSearchAgain()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var searched = DateTimeOffset.UtcNow.AddDays(-2);
        var missing = Item("1", "Dune");
        (missing.AudioStatus, missing.EbookStatus, missing.LastSearchedAt, missing.MissingSince) =
            (ListItemSideStatus.NotFound, ListItemSideStatus.NotFound, searched, searched.AddDays(-10));
        var found = Item("2", "Dune Messiah", position: 1);
        (found.AudioStatus, found.EbookStatus, found.LastSearchedAt) = (ListItemSideStatus.Selected, ListItemSideStatus.Existing, searched);
        await SeedAsync(fixture, missing, found);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync($"/Lists/Detail/{Uri.EscapeDataString(ListId)}");

        Assert.Contains("Not on MAM since", html);
        Assert.Contains("Next search:", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, ">Search again</button>"));
    }

    [Fact]
    public async Task SearchAgain_ClearsTheLastSearch_SoTheNextRunSearches()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var item = Item("1", "Dune");
        (item.AudioStatus, item.LastSearchedAt, item.MissingSince) = (ListItemSideStatus.NotFound, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-10));
        await SeedAsync(fixture, item);
        var client = fixture.CreateNoRedirectClient();
        var url = $"/Lists/Detail/{Uri.EscapeDataString(ListId)}";
        var html = await client.GetStringAsync(url);
        var token = System.Text.RegularExpressions.Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

        using var response = await client.PostAsync($"{url}?handler=SearchAgain",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["externalId"] = "1", ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var stored = await ReadItemAsync(fixture, "1");
        Assert.Null(stored.LastSearchedAt);
        Assert.NotNull(stored.MissingSince);
    }

    [Fact]
    public async Task MarkDone_ClearsTheBackOff()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        var item = Item("1", "Dune");
        (item.LastSearchedAt, item.MissingSince) = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-10));
        await SeedAsync(fixture, item);
        var client = fixture.CreateNoRedirectClient();
        var url = $"/Lists/Detail/{Uri.EscapeDataString(ListId)}";
        var html = await client.GetStringAsync(url);
        var token = System.Text.RegularExpressions.Regex.Match(html, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;

        using var _ = await client.PostAsync($"{url}?handler=MarkDone",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["externalId"] = "1", ["__RequestVerificationToken"] = token }));

        Assert.Null((await ReadItemAsync(fixture, "1")).MissingSince);
    }

    private const string FileListToml = """
        mam_id = ""

        [[file_list]]
        name = "Goodreads"
        path = "lists/export.csv"

        [[file_list.grab]]
        """;

    [Fact]
    public async Task FileList_ShowsInTheMenu_AndOnTheListsPageWithItsFile()
    {
        using var fixture = new FetchshelfWebAppFixture(FileListToml);
        var client = fixture.CreateClient();

        var home = await client.GetStringAsync("/");
        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Lists"));

        Assert.Contains(">Lists</a>", home);
        Assert.Matches(@"Goodreads</a></td>\s*<td>File lists/export.csv</td>\s*<td>0</td>", html);
    }

    [Fact]
    public async Task FileList_Detail_ShowsItsBooksWithTheirStatuses()
    {
        using var fixture = new FetchshelfWebAppFixture(FileListToml);
        const string fileListId = "file:Goodreads";
        var found = Item("isbn:9780441172719", "Dune", listId: fileListId);
        found.AudioStatus = ListItemSideStatus.NotFound;
        found.EbookStatus = ListItemSideStatus.Wanted;
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Lists.Add(new ListEntity
            {
                Id = fileListId,
                Name = "Goodreads",
                ListType = "File",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.ListItems.Add(found);
            await dbContext.SaveChangesAsync();
        }

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync($"/Lists/Detail/{Uri.EscapeDataString(fileListId)}"));

        Assert.Contains("the books in this list file", html);
        Assert.Contains("File: <code>lists/export.csv</code>", html);
        Assert.Contains("Last read from the file", html);
        Assert.Contains("Not found on MAM", html);
        Assert.Contains("costs more than the list's grab blocks allow", html);
        Assert.DoesNotContain("On Hardcover", html);
    }

    [Fact]
    public async Task FileList_NotReadYet_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(FileListToml);

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync($"/Lists/Detail/{Uri.EscapeDataString("file:Goodreads")}"));

        Assert.Contains("Fetchshelf has not read this list from its file yet.", html);
    }

    private const string BookmarksToml = """
        mam_id = ""

        [[bookmarks_list]]
        name = "Bookmarks"

        [[bookmarks_list.grab]]
        """;

    [Fact]
    public async Task BookmarksList_ShowsOnTheListsPage_AndItsRowsLinkToTheTorrentsDetails()
    {
        using var fixture = new FetchshelfWebAppFixture(BookmarksToml);
        var wanted = Item("11", "Dune", listId: "mam:bookmarks");
        wanted.AllowEbook = false;
        wanted.AudioStatus = ListItemSideStatus.NoGrabBlock;
        wanted.AudioMamId = 11;
        wanted.LastSearchedAt = DateTimeOffset.UtcNow;
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Lists.Add(new ListEntity
            {
                Id = "mam:bookmarks",
                Name = "Bookmarks",
                ListType = "MAM bookmarks",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            dbContext.ListItems.Add(wanted);
            await dbContext.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var index = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Lists"));
        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/Lists/Detail/{Uri.EscapeDataString("mam:bookmarks")}"));

        Assert.Matches(@"Bookmarks</a></td>\s*<td>MAM bookmarks</td>\s*<td>1</td>", index);
        Assert.Contains("the torrents bookmarked on MAM", html);
        Assert.Contains("Last read from MAM", html);
        Assert.Contains("<a href=\"/search/11\">Torrent details</a>", html);
        Assert.DoesNotContain("Search MAM", html);
        Assert.DoesNotContain("Next search", html);
        Assert.DoesNotContain("Search again", html);
        Assert.Contains("none of the list's grab blocks' filters takes it", html);
        Assert.DoesNotContain("Ebook:", html);
    }
}
