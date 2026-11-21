using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public class MamApiPageTests
{
    private static async Task SeedAsync(FetchshelfWebAppFixture fixture, params MamCallEntity[] calls)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.MamCalls.AddRange(calls);
        await dbContext.SaveChangesAsync();
    }

    private static MamCallEntity Call(DateTimeOffset at, int? status, string caller) => new()
    {
        StartedAtMs = at.ToUnixTimeMilliseconds(),
        DurationMs = 250,
        Method = "POST",
        Host = "www.myanonamouse.net",
        Path = "/tor/js/loadSearchJSONbasic.php",
        StatusCode = status,
        Caller = caller,
    };

    [Fact]
    public async Task MamApiPage_NoCalls_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await fixture.CreateClient().GetStringAsync("/MamApi");

        Assert.Contains("<h2>MAM API</h2>", html);
        Assert.Contains("id=\"mam-chart\"", html);
        Assert.Contains("id=\"no-calls-message\"", html);
        Assert.DoesNotContain("id=\"mam-calls\"", html);
    }

    [Fact]
    public async Task MamApiPage_ShowsTheCallsNewestFirst_AndRefreshesTheLatestWindow()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(fixture, Call(now.AddMinutes(-30), 200, "Autograb Old"), Call(now.AddMinutes(-20), 429, "Follow"));

        var html = await client.GetStringAsync("/MamApi?Span=1&Group=1");

        var table = html[html.IndexOf("id=\"mam-calls\"", StringComparison.Ordinal)..];
        Assert.True(table.IndexOf("Follow</td>", StringComparison.Ordinal) < table.IndexOf("Autograb Old</td>", StringComparison.Ordinal));
        Assert.Contains("class=\"mam-error\"", html);
        Assert.Contains("hx-trigger=\"every 15s", html);
        Assert.Contains("id=\"mam-previous\"", html);
        Assert.DoesNotContain("id=\"mam-next\"", html);
    }

    [Fact]
    public async Task MamApiPage_AnEarlierWindow_LeadsBackAndDoesNotRefresh()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();
        var end = DateTimeOffset.UtcNow.AddHours(-12).ToUnixTimeSeconds() / 600 * 600;

        var html = await client.GetStringAsync($"/MamApi?Span=6&Group=10&End={end}");

        Assert.Contains("id=\"mam-next\"", html);
        Assert.DoesNotContain("hx-trigger=", html);
        Assert.Contains($"End={end - (6 * 3600)}", html);
    }

    [Fact]
    public async Task MamApiPage_Partial_IsTheContentAlone()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await fixture.CreateClient().GetStringAsync("/MamApi?handler=Partial");

        Assert.StartsWith("<div id=\"mam-api\"", html.TrimStart());
        Assert.DoesNotContain("<h2>", html);
    }
}
