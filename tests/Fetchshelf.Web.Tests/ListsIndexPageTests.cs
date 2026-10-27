using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 8.1: the Lists page.</summary>
public class ListsIndexPageTests
{
    [Fact]
    public async Task Get_NoListsConfigured_ShowsEmptyMessage()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/lists")).Content.ReadAsStringAsync();

        Assert.Contains("no-lists-message", html);
    }

    [Fact]
    public async Task Get_ListRemovedFromConfig_AppearsAsInactive()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Lists.Add(new ListEntity
            {
                Id = "1:to-read",
                Name = "Old shelf",
                ListType = "Goodreads",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/lists")).Content.ReadAsStringAsync();

        Assert.Contains("Inactive Lists", html);
        Assert.Contains("Old shelf", html);
    }

    [Fact]
    public async Task Get_ConfiguredList_Links_To_Detail_Page()
    {
        const string configToml = """
            mam_id = ""

            [[goodreads_list]]
            url = "https://www.goodreads.com/review/list_rss/1?shelf=to-read"
            name = "My Shelf"

            [[goodreads_list.grab]]
            cost = "free"
            """;
        using var fixture = new FetchshelfWebAppFixture(configToml);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/lists")).Content.ReadAsStringAsync();

        Assert.Contains("My Shelf", html);
        Assert.Matches("/lists/1(%3A|:)to-read", html);
    }
}
