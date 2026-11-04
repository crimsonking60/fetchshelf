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
        Assert.Matches(@"Wishlist</a></td>\s*<td>2</td>\s*<td>1</td>", configured);
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
}
