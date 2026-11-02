using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Tests;

public class EventsPageTests
{
    [Fact]
    public async Task Get_SeveralEvents_ListsNewestFirst()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Kind = EventKind.Linked,
            TorrentId = "hash1",
            LinkerName = "Older Linker",
        });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Cleaned,
            TorrentId = "hash2",
            LinkerName = "Newer Linker",
        });
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();

        Assert.True(html.IndexOf("Newer Linker", StringComparison.Ordinal) < html.IndexOf("Older Linker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_EventsWithoutNames_DoNotShowDanglingBy()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Linked,
            TorrentId = "hash1",
            LibraryPath = "/lib/Author/Title",
        });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Grabbed,
            TorrentId = "hash2",
            Cost = "Ratio",
        });
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();

        Assert.Contains("Linked to /lib/Author/Title", html);
        Assert.Contains("Grabbed (cost: Ratio)", html);
        Assert.DoesNotContain("Linked by", html);
        Assert.DoesNotContain("Grabbed by", html);
    }

    [Fact]
    public async Task Get_FilterByEventType_ShowsOnlyThatType()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Linked,
            TorrentId = "hash1",
            LinkerName = "Main Library",
        });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Cleaned,
            TorrentId = "hash2",
            LinkerName = "Other Library",
        });
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events?Type=Linked")).Content.ReadAsStringAsync();

        Assert.Contains("Main Library", html);
        Assert.DoesNotContain("Other Library", html);
    }

    [Fact]
    public async Task Get_EventForTrackedTorrent_LinksWithTitleNotHash()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "0123456789abcdef0123456789abcdef01234567", OrganizerTestSupport.BuildMetadata(title: "Eugene Onegin"));
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Grabbed,
            TorrentId = "0123456789abcdef0123456789abcdef01234567",
            GrabberName = "Search",
        });
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();

        Assert.Contains(">Eugene Onegin</a>", html);
        Assert.DoesNotContain(">0123456789abcdef0123456789abcdef01234567</a>", html);
    }

    [Fact]
    public async Task Get_EventForUntrackedTorrent_FallsBackToTorrentId()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.RemovedFromMam,
            TorrentId = "gone-hash",
        });
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();

        Assert.Contains(">gone-hash</a>", html);
    }

    [Fact]
    public async Task Get_RemovedFromMamEvent_ShowsReadableTypeNotEnumName()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.RemovedFromMam,
            TorrentId = "gone-hash",
        });
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();

        Assert.Contains("<td class=\"nowrap\">Removed from MAM</td>", html);
        Assert.Contains("<option value=\"RemovedFromMam\">Removed from MAM</option>", html);
        Assert.DoesNotContain(">RemovedFromMam<", html);
    }
}
