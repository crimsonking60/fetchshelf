using System.Diagnostics;
using Fetchshelf.Data;
using Fetchshelf.Tests.Scale;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Fetchshelf.Web.Tests.Scale;

/// <summary>Times the Torrents and Events pages over a large seeded library.</summary>
[Trait("Category", "Scale")]
public sealed class PageScaleTests(ITestOutputHelper output)
{
    private const int Repeats = 3;

    private async Task<(FetchshelfWebAppFixture Fixture, ScaleData Data)> SeedAsync()
    {
        var fixture = new FetchshelfWebAppFixture();
        var data = ScaleSeeder.Build(ScaleFactAttribute.TorrentCount!.Value, "/downloads", "/library/audiobooks", "/library/ebooks");
        using (var scope = fixture.CreateScope())
        {
            await ScaleSeeder.SaveAsync(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>(), data);
        }

        output.WriteLine($"Seeded {data.Torrents.Count} torrents and {data.Events.Count} events");
        return (fixture, data);
    }

    /// <summary>Requests <paramref name="url"/> once to warm up, then reports the average of <see cref="Repeats"/> timed requests.</summary>
    private async Task<string> TimeAsync(HttpClient client, string label, string url)
    {
        var html = await client.GetStringAsync(url);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Repeats; i++)
        {
            html = await client.GetStringAsync(url);
        }

        stopwatch.Stop();
        output.WriteLine($"{label}: {stopwatch.Elapsed.TotalMilliseconds / Repeats:F0} ms per request");
        return html;
    }

    [ScaleFact]
    public async Task TorrentsPage()
    {
        var (fixture, data) = await SeedAsync();
        using var _ = fixture;
        using var client = fixture.CreateClient();
        var lastPage = (data.Torrents.Count + 49) / 50;

        var all = await TimeAsync(client, "Torrents, no filter", "/torrents?PageSize=50");
        var search = await TimeAsync(client, "Torrents, text filter", "/torrents?PageSize=50&Q=Author%2000042");
        var last = await TimeAsync(client, "Torrents, last page", $"/torrents?PageSize=50&PageNumber={lastPage}");

        Assert.Contains($"of {data.Torrents.Count}.", all);
        Assert.Contains("Author 00042", search);
        Assert.Contains($"Page {lastPage} of {lastPage}", last);
    }

    [ScaleFact]
    public async Task EventsPage()
    {
        var (fixture, data) = await SeedAsync();
        using var _ = fixture;
        using var client = fixture.CreateClient();

        var all = await TimeAsync(client, "Events, no filter", "/events?PageSize=50");
        await TimeAsync(client, "Events, type filter", "/events?PageSize=50&Type=Linked");

        Assert.Contains($"of {data.Events.Count}.", all);
    }
}
