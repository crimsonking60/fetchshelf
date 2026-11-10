using System.Net;

namespace Fetchshelf.Web.Tests;

public class FaviconTests
{
    [Theory]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/apple-touch-icon.png", "image/png")]
    public async Task Get_Icon_IsServed(string path, string contentType)
    {
        using var fixture = new FetchshelfWebAppFixture();

        var response = await fixture.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_AnyPage_LinksTheIcons()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await (await fixture.CreateClient().GetAsync("/Jobs")).Content.ReadAsStringAsync();

        // Linked through the static-asset pipeline, which adds a content fingerprint to each file name.
        Assert.Matches(@"rel=""icon"" href=""/favicon\.[a-z0-9]+\.ico"" sizes=""any""", html);
        Assert.Matches(@"rel=""icon"" href=""/favicon\.[a-z0-9]+\.svg"" type=""image/svg\+xml""", html);
        Assert.Matches(@"rel=""apple-touch-icon"" href=""/apple-touch-icon\.[a-z0-9]+\.png""", html);
    }
}
