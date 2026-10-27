using Fetchshelf.Data.Entities;
using Fetchshelf.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Fetchshelf.Web.Tests;

/// <summary>Timestamps look the same on every page: a &lt;time&gt; element that local-time.js rewrites to the browser's local time.</summary>
public class LocalTimeTests
{
    private static TagHelperOutput Render(LocalTimeTagHelper helper)
    {
        var context = new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput(
            "local-time", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(context, output);
        return output;
    }

    [Fact]
    public void Process_Value_RendersATimeElementInUtcWithTheInstantForTheBrowser()
    {
        // 14:30:05 at +02:00 is 12:30:05 UTC, whatever zone the server runs in.
        var output = Render(new LocalTimeTagHelper { Value = new DateTimeOffset(2026, 10, 27, 14, 30, 5, TimeSpan.FromHours(2)) });

        Assert.Equal("time", output.TagName);
        Assert.Equal("local-time", output.Attributes["class"].Value);
        Assert.Equal("2026-10-27T12:30:05Z", output.Attributes["datetime"].Value);
        Assert.Equal("2026-10-27 12:30:05 UTC", output.Content.GetContent());
        Assert.Equal("2026-10-27 12:30:05 UTC", output.Attributes["title"].Value);
    }

    [Fact]
    public void Process_NoValue_ShowsDashByDefault()
    {
        var output = Render(new LocalTimeTagHelper());

        Assert.Null(output.TagName);
        Assert.Equal("-", output.Content.GetContent());
    }

    [Fact]
    public void Process_NoValue_ShowsTheGivenText()
    {
        var output = Render(new LocalTimeTagHelper { Empty = "never" });

        Assert.Equal("never", output.Content.GetContent());
    }

    [Fact]
    public async Task EventsPage_RendersTimestampsAsLocalTimeElements()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = new DateTimeOffset(2026, 10, 27, 12, 30, 5, TimeSpan.Zero),
            Kind = EventKind.Cleaned,
            TorrentId = "hash1",
        });
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("<time class=\"local-time\" datetime=\"2026-10-27T12:30:05Z\"", html);
        Assert.Contains("2026-10-27 12:30:05 UTC</time>", html);
    }

    [Fact]
    public async Task TorrentDetailPage_ListsItsEventTimesWithTheSharedElement()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity { OccurredAt = DateTimeOffset.UtcNow, Kind = EventKind.Linked, TorrentId = "hash1" });
        using var client = fixture.CreateClient();

        var detail = await client.GetStringAsync("/torrents/hash1");

        Assert.Contains("<li><time class=\"local-time\"", detail);
    }

    [Fact]
    public async Task Layout_LoadsTheScriptThatLocalizesThem()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("/js/local-time.", html); // fingerprinted file name
    }
}
