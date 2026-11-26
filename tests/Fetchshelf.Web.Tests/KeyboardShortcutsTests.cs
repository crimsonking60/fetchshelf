namespace Fetchshelf.Web.Tests;

/// <summary>The footer lists the keyboard shortcuts that keys.js provides, on every page.</summary>
public class KeyboardShortcutsTests
{
    [Fact]
    public async Task Get_AnyPage_ListsTheShortcutsAndLoadsTheScript()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("id=\"shortcuts\"", html);
        Assert.Contains("data-go=\"t\"", html);
        Assert.Contains("data-go=\"j\"", html);
        Assert.Contains("data-go=\"q\"", html);
        Assert.Contains("keys.", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/torrents")]
    [InlineData("/series")]
    public async Task Get_SearchPages_HaveASearchBoxForTheSlashKey(string path)
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("name=\"Q\"", html);
    }
}
