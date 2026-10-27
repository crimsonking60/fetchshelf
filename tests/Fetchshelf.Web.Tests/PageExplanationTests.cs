namespace Fetchshelf.Web.Tests;

/// <summary>Covers the per-page explanation shown below each page's title, gated by <c>page_explanations</c>.</summary>
public class PageExplanationTests
{
    private const string EmptySearchResponseJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    private static HttpResponseMessage EmptySearchHandler(HttpRequestMessage request) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(EmptySearchResponseJson) };

    [Fact]
    public async Task Get_HomePage_ShowsExplanationByDefault()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: EmptySearchHandler);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("page-explanation", html);
    }

    [Fact]
    public async Task Get_HomePage_HidesExplanationWhenDisabled()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\npage_explanations = false\n", mamHandler: EmptySearchHandler);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("page-explanation", html);
    }

    [Fact]
    public async Task Get_EventsPage_ShowsExplanationByDefault()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();

        Assert.Contains("page-explanation", html);
    }
}
