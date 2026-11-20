using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Tests.Shared;
using Fetchshelf.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>`[seeding]` on the pages: the Torrents filter and note, the torrent page, the Account page and the Config page.</summary>
public sealed class SeedingPagesTests
{
    private const string Config = """
        mam_id = "test-mam-id"
        search_interval = 0
        link_interval = 0

        [seeding]
        interval = 0
        tag_unsatisfied = "unsat"
        """;

    private static FetchshelfWebAppFixture Fixture(string config = Config) =>
        new(config, mamHandler: request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("jsonLoad.php") ? MamAccountFixture.Json : "{}"),
        });

    private static async Task SetSeedingAsync(FetchshelfWebAppFixture fixture, string id, string title, string state, double seededHours)
    {
        await OrganizerTestSupport.SeedTorrentAsync(fixture, id, OrganizerTestSupport.BuildMetadata(title));
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var torrent = await db.Torrents.SingleAsync(t => t.Id == id);
        (torrent.SeedingState, torrent.SeedingSeconds, torrent.SeedingSince) = (state, (long)(seededHours * 3600), DateTimeOffset.UtcNow.AddDays(-5));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TorrentsPage_FiltersBySeeding_AndNotesWhatIsStillNeeded()
    {
        using var fixture = Fixture();
        await SetSeedingAsync(fixture, "hash1", "Short Book", "SeedingUnsatisfied", 41);
        await SetSeedingAsync(fixture, "hash2", "Stopped Book", "NotSeedingUnsatisfied", 10);
        await SetSeedingAsync(fixture, "hash3", "Done Book", "Satisfied", 100);
        var client = fixture.CreateClient();

        var all = await client.GetStringAsync("/torrents");
        var unsatisfied = await client.GetStringAsync("/torrents?Seeding=unsatisfied");
        var notSeeding = await client.GetStringAsync("/torrents?Seeding=NotSeedingUnsatisfied");

        Assert.Contains("<option value=\"unsatisfied\">Looks unsatisfied</option>", all);
        Assert.Contains("needs 31 h", all);
        Assert.Contains("not seeding, needs 62 h", all);
        Assert.Contains("Short Book", unsatisfied);
        Assert.Contains("Stopped Book", unsatisfied);
        Assert.DoesNotContain("Done Book", unsatisfied);
        Assert.DoesNotContain("Short Book", notSeeding);
        Assert.Contains("Stopped Book", notSeeding);
    }

    [Fact]
    public async Task TorrentsPage_WithoutASeedingSection_OffersNoSeedingFilter()
    {
        using var fixture = Fixture("mam_id = \"\"\n");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1");

        Assert.DoesNotContain("name=\"Seeding\"", await fixture.CreateClient().GetStringAsync("/torrents"));
    }

    [Fact]
    public async Task TorrentPage_ShowsTheEstimate_AndWarnsBeforeUnlinkAndRemove()
    {
        using var fixture = Fixture();
        await SetSeedingAsync(fixture, "hash1", "Short Book", "SeedingUnsatisfied", 41);
        await SetSeedingAsync(fixture, "hash2", "Done Book", "Satisfied", 100);
        var client = fixture.CreateClient();

        var shortPage = WebUtility.HtmlDecode(await client.GetStringAsync("/torrents/hash1"));
        var donePage = WebUtility.HtmlDecode(await client.GetStringAsync("/torrents/hash2"));

        // Not in qBittorrent here, so the stored estimate: not seeding now.
        Assert.Contains("Not seeding, and seeded 41 of 72 hours: seed it 31 more by", shortPage);
        Assert.Contains("data-confirm=\"It has seeded 41 of the 72 hours MAM requires; removing it from qBittorrent before then can make it a hit-and-run. Remove this book", shortPage);
        Assert.Contains("Seeded 100 hours: satisfied.", donePage);
        Assert.Contains("data-confirm=\"Remove this book", donePage);
    }

    [Fact]
    public async Task AccountPage_ShowsTheEstimateNextToMamsCounts()
    {
        using var fixture = Fixture();
        await SetSeedingAsync(fixture, "hash1", "Short Book", "SeedingUnsatisfied", 41);
        await SetSeedingAsync(fixture, "hash2", "Stopped Book", "NotSeedingUnsatisfied", 10);
        var client = fixture.CreateClient();
        using (var scope = fixture.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MamClient>().GetAccountStateAsync();
        }

        var html = System.Text.RegularExpressions.Regex.Replace(await client.GetStringAsync("/Account"), @"\s+", " ");

        Assert.Contains("Fetchshelf's estimate from qBittorrent's seeding time:", html);
        Assert.Contains(">1 seeding</a> and <a href=\"/Torrents?Seeding=NotSeedingUnsatisfied\">1 not seeding</a> of its torrents look unsatisfied.", html);
        Assert.Contains("MAM's counts are what matter.", html);
    }

    [Fact]
    public async Task ConfigPage_AndJobs_ShowTheSeedingSection()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();

        var config = await client.GetStringAsync("/config");
        await client.GetStringAsync("/Jobs");

        Assert.Contains("72 hours within 30 days", config);
        Assert.Contains("<td>unsat</td>", config);
        Assert.NotNull(fixture.JobRegistry.Get("Seeding"));
    }

    [Fact]
    public void JobOutcome_SaysWhatTheRunFound() =>
        Assert.Equal(
            "120 torrents checked: 12 look unsatisfied (2 not seeding), 1 past the period, 3 newly satisfied, on_cleaned applied to 1",
            JobOutcomes.Seeding(new SeedingRunSummary(120, 12, 2, 1, 3, 1)).Summary);
}
