using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Clients.QBittorrent;
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
    public async Task BurndownPage_StartsAtTheEstimate_ThenLists73Hours_ForFetchshelfEveryMamTorrentAndMamsCount()
    {
        var added = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds();
        List<QBittorrentTorrent> inClient =
        [
            // MAM's tracker, 10 hours to go.
            new() { Hash = "mam1", Name = "A", SavePath = "/d", State = "uploading", Progress = 1, SeedingTime = 62 * 3600, AddedOn = added, Tracker = "https://t.myanonamouse.net/tracker.php/abc/announce" },
            // No tracker has answered, but MAM's comment: not seeding, so it counts until its period ends.
            new() { Hash = "mam2", Name = "B", SavePath = "/d", State = "stoppedUP", Progress = 1, SeedingTime = 3600, AddedOn = added, Comment = "MID=123" },
            // Another tracker, and a MAM torrent still downloading: neither counts.
            new() { Hash = "other", Name = "C", SavePath = "/d", State = "uploading", Progress = 1, AddedOn = added, Tracker = "https://tracker.example/announce" },
            new() { Hash = "mam3", Name = "D", SavePath = "/d", State = "downloading", Progress = 0.5, AddedOn = added, Tracker = "https://t.myanonamouse.net/announce" },
        ];
        using var fixture = new FetchshelfWebAppFixture(
            Config + "\n[[qbittorrent]]\nurl = \"http://qbittorrent.test\"\nusername = \"admin\"\npassword = \"adminadmin\"\n",
            qbittorrentHandler: request => request.RequestUri!.AbsolutePath.Contains("torrents/info")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = System.Net.Http.Json.JsonContent.Create(inClient) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") },
            mamHandler: request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("jsonLoad.php") ? MamAccountFixture.Json : "{}"),
            });
        await SetSeedingAsync(fixture, "hash1", "Short Book", "SeedingUnsatisfied", 41);
        await SetSeedingAsync(fixture, "hash2", "Stopped Book", "NotSeedingUnsatisfied", 10);
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            // The Seeding job estimated half an hour ago: the table starts with that estimate.
            await db.Torrents.ForEachAsync(t => t.SeedingCheckedAt = DateTimeOffset.UtcNow.AddMinutes(-30));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<MamClient>().GetAccountStateAsync();
        }

        var client = fixture.CreateClient();
        var html = System.Text.RegularExpressions.Regex.Replace(await client.GetStringAsync("/unsatisfied"), @"\s+", " ");
        var estimate = System.Text.RegularExpressions.Regex.Match(
            html, "<tr> <td><time[^>]*data-minute[^>]*>[^<]+</time> ?<span class=\"muted\"> ?\\(the estimate\\)</span> ?</td> <td class=\"num\"[^>]*>(\\d+)</td> <td class=\"num\">-</td> <td class=\"num\">-</td>");
        var rows = System.Text.RegularExpressions.Regex.Matches(
            html, "<tr> <td><time[^>]*data-minute[^>]*>[^<]+</time></td>"
            + " <td class=\"num( fewer)?\"[^>]*>(\\d+)</td> <td class=\"num( fewer)?\"[^>]*>(\\d+)</td> <td class=\"num( fewer)?\"[^>]*>(\\d+)</td>");

        Assert.Contains("<th class=\"num\">Fetchshelf unsatisfied</th><th class=\"num\">In qBittorrent</th><th class=\"num\">MAM projected</th>", html);
        Assert.Contains(">1 seeding</a> and <a href=\"/Torrents?Seeding=NotSeedingUnsatisfied\">1 not seeding</a> of its torrents look unsatisfied.", html);
        Assert.Contains("MAM counts 14 and 0.", html);
        Assert.Matches("<p id=\"mam-reported\">MAM's own count: 14 unsatisfied, read <time", html);
        Assert.True(estimate.Success);
        Assert.Equal("2", estimate.Groups[1].Value);
        Assert.Equal(73, rows.Count);
        Assert.Equal(("2", "2", "14"), (rows[0].Groups[2].Value, rows[0].Groups[4].Value, rows[0].Groups[6].Value));
        Assert.Equal(("1", "1", "13"), (rows[72].Groups[2].Value, rows[72].Groups[4].Value, rows[72].Groups[6].Value));
        var fetchshelfFewer = Assert.Single(rows, row => row.Groups[1].Success);
        var inClientFewer = Assert.Single(rows, row => row.Groups[3].Success);
        var mamFewer = Assert.Single(rows, row => row.Groups[5].Success);
        Assert.Contains("title=\"1 satisfied\"", fetchshelfFewer.Value);
        Assert.InRange(rows.ToList().IndexOf(inClientFewer), 10, 11);
        Assert.Equal(inClientFewer.Index, mamFewer.Index);
        Assert.Contains("Unsatisfied burndown</a>", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task BurndownPage_WithoutASeedingSection_IsNotThere()
    {
        using var fixture = Fixture("mam_id = \"\"\n");
        var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/unsatisfied")).StatusCode);
        Assert.DoesNotContain("Unsatisfied burndown", await client.GetStringAsync("/"));
    }

    [Fact]
    public void JobOutcome_SaysWhatTheRunFound() =>
        Assert.Equal(
            "120 torrents checked: 12 look unsatisfied (2 not seeding), 1 past the period, 3 newly satisfied, on_cleaned applied to 1",
            JobOutcomes.Seeding(new SeedingRunSummary(120, 12, 2, 1, 3, 1)).Summary);
}
