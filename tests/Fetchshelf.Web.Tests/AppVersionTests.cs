namespace Fetchshelf.Web.Tests;

/// <summary>The running version, as shown on the Config page and logged at startup.</summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("2026.10.2", "2026.10.2")]
    [InlineData("develop-14747e2", "develop-14747e2")]
    [InlineData("dev+14747e2a1b2c3d4e5f60718293a4b5c6d7e8f90", "dev")]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    public void FromInformationalVersion_WithCommitSuffix_DropsTheSuffix(string? informationalVersion, string expected)
    {
        Assert.Equal(expected, AppVersion.FromInformationalVersion(informationalVersion));
    }

    [Fact]
    public void Current_LocalBuild_IsDev()
    {
        Assert.Equal("dev", AppVersion.Current);
    }

    [Fact]
    public async Task Get_ConfigPage_ShowsTheVersion()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Config");

        Assert.Contains($"Fetchshelf version <strong>{AppVersion.Current}</strong>", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Jobs")]
    [InlineData("/Torrents")]
    public async Task EveryPage_EndsWithAFooter_WithTheVersionOnTheLeftAndTheThemeOnTheRight(string page)
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await fixture.CreateClient().GetStringAsync(page);

        var header = html[html.IndexOf("<header", StringComparison.Ordinal)..html.IndexOf("</header>", StringComparison.Ordinal)];
        var footer = html[html.IndexOf("<footer class=\"site-footer\">", StringComparison.Ordinal)..];
        Assert.True(html.IndexOf("</main>", StringComparison.Ordinal) < html.IndexOf("<footer", StringComparison.Ordinal), "The footer comes after the page");
        Assert.DoesNotContain("theme-select", header);
        Assert.Contains($"<span id=\"site-version\">Fetchshelf version {AppVersion.Current}</span>", footer);
        Assert.True(
            footer.IndexOf("site-version", StringComparison.Ordinal) < footer.IndexOf("id=\"theme-select\"", StringComparison.Ordinal),
            "The version comes before the theme selector");
    }
}
