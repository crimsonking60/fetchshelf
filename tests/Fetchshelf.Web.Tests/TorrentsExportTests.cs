using System.Text.Json;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Tests;

/// <summary>The Torrents page's Export links: every page of the list as it is filtered, as CSV or JSON.</summary>
public sealed class TorrentsExportTests
{
    private static async Task<FetchshelfWebAppFixture> SeededAsync()
    {
        var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(
            fixture, "hash1", OrganizerTestSupport.BuildMetadata("The Way of Kings", ["Brandon Sanderson"]), libraryPath: "/library/Brandon Sanderson/The Way of Kings");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Project Hail Mary", ["Andy Weir"]));
        await OrganizerTestSupport.SeedEventAsync(fixture, new EventEntity
        {
            OccurredAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
            TorrentId = "hash1",
            Kind = EventKind.Linked,
            LibraryPath = "/library/Brandon Sanderson/The Way of Kings",
        });
        return fixture;
    }

    [Fact]
    public async Task Page_HasExportLinks_ThatKeepTheFilters()
    {
        using var fixture = await SeededAsync();
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/torrents?Q=kings");

        Assert.Matches(@"href=""/Torrents\?Q=kings&amp;[^""]*format=csv&amp;handler=Export""", html);
        Assert.Matches(@"href=""/Torrents\?Q=kings&amp;[^""]*format=json&amp;handler=Export""", html);
    }

    [Fact]
    public async Task ExportCsv_WritesEveryTorrent_AsAnAttachment()
    {
        using var fixture = await SeededAsync();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/torrents?handler=Export&format=csv");
        var csv = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"^attachment; filename=""fetchshelf-torrents-\d{8}-\d{6}\.csv""$", response.Content.Headers.ContentDisposition!.ToString());
        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("mam_id,title,authors,", lines[0]);
        Assert.Contains(lines, line => line.Contains(",The Way of Kings,Brandon Sanderson,", StringComparison.Ordinal)
            && line.Contains(",linked,", StringComparison.Ordinal)
            && line.EndsWith(",2026-11-02T09:00:00Z", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(",Project Hail Mary,Andy Weir,", StringComparison.Ordinal) && line.Contains(",waiting,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportJson_TakesTheSearchAndFilters()
    {
        using var fixture = await SeededAsync();
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/torrents?handler=Export&format=json&Q=hail&PageSize=1");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var row = Assert.Single(json.EnumerateArray());
        Assert.Equal("Project Hail Mary", row.GetProperty("title").GetString());
        Assert.Equal("waiting", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ExportJson_IgnoresThePage_AndWritesEveryMatch()
    {
        using var fixture = await SeededAsync();
        using var client = fixture.CreateClient();

        var json = JsonDocument.Parse(await client.GetStringAsync("/torrents?handler=Export&format=json&PageSize=1&PageNumber=2")).RootElement;

        Assert.Equal(2, json.GetArrayLength());
    }
}
