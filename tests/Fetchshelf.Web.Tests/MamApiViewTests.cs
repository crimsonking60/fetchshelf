using Fetchshelf.Web.Pages.MamApi;

namespace Fetchshelf.Web.Tests;

public class MamApiViewTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static MamApiCall Call(DateTimeOffset at, int? status = 200, string? failure = null, int ms = 300, string path = "/tor/js/loadSearchJSONbasic.php", string? caller = "Autograb New") =>
        new(at, ms, "POST", "www.myanonamouse.net", path, status, failure, caller);

    [Fact]
    public void Window_Now_EndsAtTheEndOfTheCurrentInterval()
    {
        var (start, end) = MamApiView.Window(Noon.AddMinutes(3).AddSeconds(5), null, TimeSpan.FromHours(1), TimeSpan.FromMinutes(10));

        Assert.Equal((Noon.AddMinutes(-50), Noon.AddMinutes(10)), (start, end));
    }

    [Fact]
    public void Window_OnABoundary_StaysThere()
    {
        var (start, end) = MamApiView.Window(Noon.AddHours(5), Noon, TimeSpan.FromHours(6), TimeSpan.FromMinutes(30));

        Assert.Equal((Noon.AddHours(-6), Noon), (start, end));
    }

    [Fact]
    public void Build_CountsCallsPerInterval_AndMarksOnlyTheErrors()
    {
        var calls = new[]
        {
            Call(Noon.AddMinutes(-59)),
            Call(Noon.AddMinutes(-55), status: 429),
            Call(Noon.AddMinutes(-5), status: 503),
            Call(Noon.AddMinutes(-4), status: null, failure: "No answer in time"),
            Call(Noon.AddMinutes(-3), status: null, failure: "Cancelled"),
            Call(Noon.AddMinutes(-70)), // before the window
            Call(Noon), // the window ends before it
        };

        var view = MamApiView.Build(calls, Noon.AddHours(-1), Noon, TimeSpan.FromMinutes(10));

        Assert.Equal([2, 0, 0, 0, 0, 3], view.Buckets.Select(b => b.Calls.Count));
        Assert.Equal([429, 503, null], view.Errors.Select(c => c.StatusCode));
        Assert.Equal(Noon.AddMinutes(-3), view.NewestFirst[0].StartedAt);
        Assert.Equal((5, 1, 1, 1, 1, 1), (view.Stats.Calls, view.Stats.Ok, view.Stats.ClientErrors, view.Stats.ServerErrors, view.Stats.NoAnswer, view.Stats.RateLimited));
        Assert.Equal(Noon.AddMinutes(-55), view.Stats.LastRateLimitedAt);
    }

    [Fact]
    public void Build_FindsTheBusiestStretch_AndTheShortestGap()
    {
        var calls = new[] { 0, 30, 40, 45, 70, 79, 81, 300 }.Select(s => Call(Noon.AddHours(-1).AddSeconds(s))).ToList();

        var stats = MamApiView.Build(calls, Noon.AddHours(-1), Noon, TimeSpan.FromMinutes(1)).Stats;

        // 30 s to 81 s is under a minute and holds six calls.
        Assert.Equal((6, Noon.AddHours(-1).AddSeconds(30)), stats.BusiestMinute);
        Assert.Equal(TimeSpan.FromSeconds(2), stats.ShortestGap);
    }

    [Fact]
    public void Build_GroupsByEndpoint_WithCoversAndDownloadsTogether()
    {
        var calls = new[]
        {
            Call(Noon.AddMinutes(-3), path: "/tor/download.php/<hash>", ms: 100),
            Call(Noon.AddMinutes(-2), path: "/t/p/small/1.webp", ms: 50, caller: null),
            Call(Noon.AddMinutes(-1), path: "/t/p/small/2.webp", ms: 70, caller: null, status: 404),
        };

        var stats = MamApiView.Build(calls, Noon.AddHours(-1), Noon, TimeSpan.FromMinutes(10)).Stats;

        Assert.Equal([new MamApiBreakdown("Cover images", 2, 1, 50), new MamApiBreakdown("/tor/download.php", 1, 0, 100)], stats.ByEndpoint);
        Assert.Equal("No job (a page)", stats.ByCaller[0].Label);
    }

    [Fact]
    public void Chart_HasABarPerInterval_AndARedLinePerError()
    {
        var view = MamApiView.Build([Call(Noon.AddMinutes(-30)), Call(Noon.AddMinutes(-20), status: 429)], Noon.AddHours(-1), Noon, TimeSpan.FromMinutes(1));

        var svg = MamApiChartSvg.Build(view);

        Assert.Equal(60, System.Text.RegularExpressions.Regex.Count(svg, "class=\"mam-hit\""));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(svg, "class=\"mam-bar\""));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "class=\"mam-error\""));
        Assert.Contains("data-tip=\"HTTP 429\nPOST /tor/js/loadSearchJSONbasic.php\nBy Autograb New\n300 ms\"", svg);
    }
}
