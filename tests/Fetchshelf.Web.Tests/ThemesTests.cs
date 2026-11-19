using System.Net;
using Fetchshelf.Web;

namespace Fetchshelf.Web.Tests;

public class ThemesTests
{
    [Theory]
    [InlineData("dark-warm")]
    [InlineData("dark-cool")]
    [InlineData("light-warm")]
    [InlineData("light-cool")]
    [InlineData("system")]
    public void Resolve_KnownTheme_IsReturnedUnchanged(string id) =>
        Assert.Equal(id, Themes.Resolve(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("neon")]
    [InlineData("\"><script>")]
    public void Resolve_MissingOrUnknown_FallsBackToMatchSystem(string? id) =>
        Assert.Equal("system", Themes.Resolve(id));

    [Fact]
    public async Task Layout_RendersThemeFromCookie_AndDefaultsToMatchSystem()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var defaultHtml = await client.GetStringAsync("/Jobs");
        Assert.Contains("<html lang=\"en\" data-theme=\"system\">", defaultHtml);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/Jobs");
        request.Headers.Add("Cookie", "fs-theme=light-cool");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"light-cool\"", html);
        Assert.Matches("<a href=\"/theme\\?name=light-cool[^\"]*\" data-theme-choice=\"light-cool\" aria-current=\"true\">Light Cool</a>", html);
        Assert.DoesNotContain("data-theme-choice=\"dark-warm\" aria-current", html);
    }

    [Fact]
    public async Task ThemeEndpoint_SetsCookie_AndRedirectsToLocalReturnUrl()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateNoRedirectClient();

        var response = await client.GetAsync("/theme?name=dark-cool&returnUrl=%2FJobs");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Jobs", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("fs-theme=dark-cool", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    public async Task GetTheme_NonLocalReturnUrl_RedirectsHomeInstead(string returnUrl)
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateNoRedirectClient();

        var response = await client.GetAsync($"/theme?name=bogus&returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("fs-theme=system", StringComparison.Ordinal));
    }
}
