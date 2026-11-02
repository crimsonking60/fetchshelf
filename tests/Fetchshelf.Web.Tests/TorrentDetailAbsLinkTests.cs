using Fetchshelf.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Torrent detail page's Audiobookshelf link.</summary>
public class TorrentDetailAbsLinkTests
{
    private const string ConfigToml = """
        mam_id = ""

        [audiobookshelf]
        url = "http://abs.local"
        token = "abs-token"
        """;

    [Fact]
    public async Task Get_TorrentWithStoredAbsId_ShowsAudiobookshelfLink()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        await SetAbsIdAsync(fixture, "hash1", "item1");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/torrents/hash1")).Content.ReadAsStringAsync();

        Assert.Contains("http://abs.local/audiobookshelf/item/item1", html);
    }

    [Fact]
    public async Task Get_TorrentWithNoStoredAbsId_ShowsNoAudiobookshelfLink()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigToml);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/torrents/hash1")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("id=\"abs-link\"", html);
    }

    [Fact]
    public async Task Get_AudiobookshelfNotConfigured_ShowsNoAudiobookshelfLinkEvenWithStoredAbsId()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\n");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");
        await SetAbsIdAsync(fixture, "hash1", "item1");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/torrents/hash1")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("id=\"abs-link\"", html);
    }

    private static async Task SetAbsIdAsync(FetchshelfWebAppFixture fixture, string torrentId, string absId)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var torrent = await dbContext.Torrents.SingleAsync(t => t.Id == torrentId);
        torrent.AbsId = absId;
        await dbContext.SaveChangesAsync();
    }
}
