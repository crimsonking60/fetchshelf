namespace Fetchshelf.Web.Tests;

/// <summary>The page shown when a request fails unexpectedly.</summary>
public class ErrorPageTests
{
    [Fact]
    public async Task Get_ErrorPage_PointsToTheLogWithoutFrameworkAdvice()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/Error");

        Assert.Contains("Something went wrong", html);
        Assert.Contains("docker logs fetchshelf", html);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", html);
        Assert.DoesNotContain("Development Mode", html);
    }
}
