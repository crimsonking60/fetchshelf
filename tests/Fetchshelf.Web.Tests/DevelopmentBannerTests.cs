namespace Fetchshelf.Web.Tests;

/// <summary>The red bar `development_banner` puts at the top of every page.</summary>
public class DevelopmentBannerTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/torrents")]
    [InlineData("/config")]
    public async Task On_EveryPageStartsWithTheBanner(string url)
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\ndevelopment_banner = true\n");

        var html = await fixture.CreateClient().GetStringAsync(url);

        Assert.Contains("<div class=\"development-banner\" aria-hidden=\"true\"></div>", html);
    }

    [Fact]
    public async Task Off_ByDefault()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await fixture.CreateClient().GetStringAsync("/");

        Assert.DoesNotContain("development-banner", html);
    }
}
