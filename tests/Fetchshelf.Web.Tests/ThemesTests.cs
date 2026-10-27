using System.Net;
using Fetchshelf.Web;

namespace Fetchshelf.Web.Tests;

public class ThemesTests
{
    [Theory]
    [InlineData("dark-bright")]
    [InlineData("dark-subdued")]
    [InlineData("light-bright")]
    [InlineData("light-subdued")]
    public void Resolve_KnownTheme_IsReturnedUnchanged(string id) =>
        Assert.Equal(id, Themes.Resolve(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("neon")]
    [InlineData("\"><script>")]
    public void Resolve_MissingOrUnknown_FallsBackToDarkBright(string? id) =>
        Assert.Equal("dark-bright", Themes.Resolve(id));

    [Fact]
    public async Task Layout_RendersThemeFromCookie_AndDefaultsToDarkBright()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var defaultHtml = await client.GetStringAsync("/Jobs");
        Assert.Contains("<html lang=\"en\" data-theme=\"dark-bright\">", defaultHtml);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/Jobs");
        request.Headers.Add("Cookie", "fs-theme=light-subdued");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"light-subdued\"", html);
        Assert.Contains("<option value=\"light-subdued\" selected=\"selected\">", html);
    }

    [Fact]
    public async Task ThemeEndpoint_SetsCookie_AndRedirectsToLocalReturnUrl()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateNoRedirectClient();

        var response = await client.GetAsync("/theme?name=dark-subdued&returnUrl=%2FJobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Jobs", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("fs-theme=dark-subdued", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    public async Task ThemeEndpoint_IgnoresNonLocalReturnUrl(string returnUrl)
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateNoRedirectClient();

        var response = await client.GetAsync($"/theme?name=bogus&returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("fs-theme=dark-bright", StringComparison.Ordinal));
    }
}
