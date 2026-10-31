using System.Net;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Tests;

/// <summary>The /health endpoint and the container health probe that calls it.</summary>
public class HealthTests
{
    [Fact]
    public async Task Get_Health_IsHealthy()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("0.0.0.0", 3157, "http://127.0.0.1:3157/health")]
    [InlineData("", 3157, "http://127.0.0.1:3157/health")]
    [InlineData("::", 8000, "http://[::1]:8000/health")]
    [InlineData("192.168.1.10", 3157, "http://192.168.1.10:3157/health")]
    [InlineData("fd00::10", 3157, "http://[fd00::10]:3157/health")]
    [InlineData("fetchshelf.lan", 9000, "http://fetchshelf.lan:9000/health")]
    public void ProbeUrl_UsesTheConfiguredPort_AndLoopbackForWildcardHosts(string webHost, int webPort, string expected)
    {
        var settings = new FetchshelfSettings { WebHost = webHost, WebPort = webPort };

        Assert.Equal(expected, HealthProbe.ProbeUrl(settings));
    }

    [Fact]
    public async Task RunAsync_NoConfigurationFile_IsUnhealthyAndCreatesNothing()
    {
        var configFilePath = Path.Combine(Path.GetTempPath(), $"fetchshelf-probe-{Guid.NewGuid():N}", "config.toml");

        var exitCode = await HealthProbe.RunAsync(configFilePath, TimeSpan.FromSeconds(1));

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(configFilePath));
    }
}
