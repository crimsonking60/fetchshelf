using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// On a phone a table scrolls sideways in its own box, where a sticky heading would stick inside the box instead of under
/// the page's header. There table-heads.js keeps the column names under the header, and the stylesheet stops them sticking.
/// </summary>
public class TableHeadsTests
{
    [Fact]
    public async Task EveryPage_LoadsTheScript_AndOnAPhoneTheHeadingsDoNotStickInsideTheTable()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/torrents");

        var script = await client.GetStringAsync(Regex.Match(page, "src=\"(/js/table-heads[^\"]*)\"").Groups[1].Value);
        var css = await client.GetStringAsync(Regex.Match(page, "href=\"(/css/site[^\"]*)\"").Groups[1].Value);

        Assert.Contains("matchMedia('(max-width: 40rem)')", script);
        Assert.Contains("translateY(", script);
        Assert.Matches(@"@media \(max-width: 40rem\) \{\s*main table \{ display: block; overflow-x: auto; \}\s*thead th \{ position: static; \}", css);
    }
}
