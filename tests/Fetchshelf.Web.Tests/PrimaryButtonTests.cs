using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// One primary button per page region, set in the markup: a search or filter form's submit, an edit form's Save, the
/// action a page is for. Bulk bars, row buttons and one-off actions stay plain.
/// </summary>
public partial class PrimaryButtonTests
{
    [GeneratedRegex(@"<button[^>]*class=""primary""[^>]*>([^<]*)</button>")]
    private static partial Regex PrimaryButtonPattern();

    private static List<string> PrimaryButtons(string html) =>
        [.. PrimaryButtonPattern().Matches(html).Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim())];

    [Theory]
    [InlineData("/", "Search")]
    [InlineData("/torrents", "Search")]
    [InlineData("/series", "Search")]
    [InlineData("/jobs/history", "Filter")]
    [InlineData("/librarycheck", "Run the check")]
    public async Task Get_Page_HasOnePrimaryButton_ForItsMainAction(string url, string label)
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync(url);

        Assert.Equal([label], PrimaryButtons(html));
    }

    [Fact]
    public async Task Get_TorrentDetail_MarksRefreshAndRelinkAmongTheActions()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using var client = fixture.CreateClient();

        var primaries = PrimaryButtons(await client.GetStringAsync("/torrents/hash1"));

        Assert.Contains("Refresh and relink", primaries);
        Assert.DoesNotContain("Refresh", primaries);
        Assert.DoesNotContain("Remove record", primaries);
    }

    [Fact]
    public async Task Get_TorrentEdit_MarksSave()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents/hash1/edit");

        Assert.Equal(["Save"], PrimaryButtons(html));
    }

    [Fact]
    public async Task Get_TorrentsWithARow_LeavesTheBulkBarPlain()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("A Book", ["An Author"]));
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents");

        Assert.Equal(["Search"], PrimaryButtons(html));
    }

    [Fact]
    public async Task Stylesheet_StylesOnlyTheMarkedButtonAsPrimary()
    {
        // The accent fill used to come from selectors (every GET form's and edit form's submit), so which button looked
        // primary depended on how a page nested its forms.
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/");
        var cssUrl = Regex.Match(page, "href=\"(/css/site[^\"]*)\"").Groups[1].Value;

        var css = await client.GetStringAsync(cssUrl);

        Assert.Contains("button.primary {", css);
        Assert.DoesNotContain("form[method=\"get\"] > button", css);
        Assert.DoesNotContain(".edit-form > button[type=\"submit\"]:hover", css);
    }
}
