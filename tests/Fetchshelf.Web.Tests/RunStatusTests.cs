namespace Fetchshelf.Web.Tests;

/// <summary>The menu bar's "running" pill: a hidden link on every page, and an endpoint listing the jobs that run.</summary>
public class RunStatusTests
{
    [Fact]
    public async Task Get_AnyPage_HasTheHiddenStatusPillAndItsScript()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("id=\"run-status\"", html);
        Assert.Contains("run-status.", html);
    }

    [Fact]
    public async Task Get_Running_ListsNoJobsWhenNothingRuns()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/jobs?handler=Running");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }
}
