using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Tests;

public class EventsPageTests
{
    /// <summary>The events table alone: the filter dropdowns above it name grabbers and libraries too.</summary>
    private static string Table(string html) =>
        html.Contains("<tbody>", StringComparison.Ordinal) ? html[html.IndexOf("<tbody>", StringComparison.Ordinal)..html.IndexOf("</tbody>", StringComparison.Ordinal)] : string.Empty;

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
        Assert.DoesNotContain("Linked by", Table(html));
        Assert.DoesNotContain("Grabbed by", Table(html));
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

        Assert.Contains("Main Library", Table(html));
        Assert.DoesNotContain("Other Library", Table(html));
    }

    [Fact]
    public async Task Get_GrabberAndLibraryDropdowns_OfferTheNamesEventsRecorded()
    {
        using var fixture = new FetchshelfWebAppFixture();
        foreach (var (kind, grabber, linker) in new[]
        {
            (EventKind.Grabbed, "Freeleech", (string?)null),
            (EventKind.Grabbed, "Bookmarks", null),
            (EventKind.Grabbed, "Freeleech", null),
            (EventKind.Linked, null, "Audiobooks"),
        })
        {
            await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Kind = kind,
                TorrentId = "hash",
                GrabberName = grabber,
                LinkerName = linker,
            });
        }

        var html = await fixture.CreateClient().GetStringAsync("/events?Grabber=Freeleech");

        Assert.Contains("<option value=\"\">Grabbed by any job</option><option value=\"Bookmarks\">Bookmarks</option><option value=\"Freeleech\" selected=\"selected\">Freeleech</option>",
            CollapseWhitespace(html));
        Assert.Contains("<option value=\"Audiobooks\">Audiobooks</option>", html);
        Assert.Equal(2, Table(html).Split("Grabbed by Freeleech").Length - 1);
    }

    [Fact]
    public async Task Get_FilterByField_MatchesTheChangedFieldNotAValueThatNamesIt()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Updated,
            TorrentId = "hash1",
            FieldChangesJson = System.Text.Json.JsonSerializer.Serialize(new[] { new Fetchshelf.Core.Metadata.FieldChange("Authors", "A. Writer", "Anne Writer") }),
        });
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Updated,
            TorrentId = "hash2",
            FieldChangesJson = System.Text.Json.JsonSerializer.Serialize(new[] { new Fetchshelf.Core.Metadata.FieldChange("Title", "Authors", "The Authors") }),
        });

        var html = await fixture.CreateClient().GetStringAsync("/events?Type=Updated&Field=Authors");

        Assert.Contains("Anne Writer", Table(html));
        Assert.DoesNotContain("The Authors", Table(html));
    }

    [Fact]
    public async Task FieldFilter_IsOfferedAndAppliedOnlyForUpdatedEvents()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = DateTimeOffset.UtcNow,
            Kind = EventKind.Linked,
            TorrentId = "hash1",
            LinkerName = "Audiobooks",
        });
        var client = fixture.CreateClient();

        var all = await client.GetStringAsync("/events?Field=Title");
        var linked = await client.GetStringAsync("/events?Type=Linked&Field=Title");
        var updated = await client.GetStringAsync("/events?Type=Updated");

        Assert.DoesNotContain("name=\"Field\"", all);
        Assert.Contains("Linked by Audiobooks", Table(all)); // the field is ignored without the Updated type
        Assert.DoesNotContain("name=\"Field\"", linked);
        Assert.Contains("Linked by Audiobooks", Table(linked));
        Assert.Contains("name=\"Field\"", updated);
        Assert.Contains("<option value=\"LegacyCategory\">Category</option>", updated);
    }

    [Fact]
    public async Task ClearFilters_ShowsOnlyWithAFilter_AndKeepsThePageSize()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var unfiltered = await client.GetStringAsync("/events?PageSize=50");
        var filtered = await client.GetStringAsync("/events?Type=Linked&PageSize=50");
        var fieldWithoutUpdated = await client.GetStringAsync("/events?Field=Title&PageSize=50");

        Assert.DoesNotContain("clear-filters", unfiltered);
        Assert.DoesNotContain("clear-filters", fieldWithoutUpdated);
        Assert.Contains("<a id=\"clear-filters\" href=\"/Events?PageSize=50\">Clear filters</a>", filtered);
    }

    private static string CollapseWhitespace(string html) => System.Text.RegularExpressions.Regex.Replace(html, @">\s+<", "><");

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
