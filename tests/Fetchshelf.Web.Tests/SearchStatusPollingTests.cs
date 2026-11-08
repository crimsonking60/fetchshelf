using System.Net;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Search page's status badge, which keeps asking while a torrent is on its way into the library.</summary>
public class SearchStatusPollingTests
{
    private static async Task SeedTorrentAsync(FetchshelfWebAppFixture fixture, string? libraryPath, string? clientStatus = "stalledUP") =>
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: libraryPath, clientStatus: clientStatus);

    private static async Task SeedSelectionAsync(FetchshelfWebAppFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
        {
            MamId = 1,
            DownloadLink = "dl1",
            CostDecision = "Ratio",
            MetadataJson = "{}",
            NormalizedTitleKey = "way of kings",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Status_Downloaded_ShowsItAndAsksAgainInAMinute()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedTorrentAsync(fixture, libraryPath: null);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=10");

        Assert.Contains("status-downloaded", html);
        Assert.Contains("hx-get=\"/?mamId=1&amp;round=11&amp;handler=Status\"", html);
        Assert.Contains("hx-trigger=\"load delay:60s\"", html);
    }

    [Fact]
    public async Task Status_FirstMinute_AsksEveryTenSeconds()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSelectionAsync(fixture);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=2");

        Assert.Contains("hx-trigger=\"load delay:10s\"", html);
    }

    private const string ConfigWithQBittorrent = """
        mam_id = ""

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"
        """;

    /// <summary>A qBittorrent that has hash1 in the given state; null makes every request fail.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> QBittorrentWithHash1(string? state, double progress) => request =>
    {
        if (state is null)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (request.RequestUri!.AbsolutePath.Contains("auth/login"))
        {
            var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            login.Headers.Add("Set-Cookie", "SID=abc; path=/");
            return login;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new[]
            {
                new Clients.QBittorrent.QBittorrentTorrent { Hash = "hash1", Name = "T", SavePath = "/d", State = state, Progress = progress },
            }),
        };
    };

    [Fact]
    public async Task Status_DownloadingThatQBittorrentHasFinished_ShowsDownloaded_StoresIt_AndStartsTheLinkJob()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: QBittorrentWithHash1("stalledUP", 1.0));
        var linkStarted = new TaskCompletionSource();
        fixture.JobRegistry.Register("Link", _ =>
        {
            linkStarted.TrySetResult();
            return Task.CompletedTask;
        });
        await SeedTorrentAsync(fixture, libraryPath: null, clientStatus: null);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=1");

        Assert.Contains("status-downloaded", html);
        Assert.Same(linkStarted.Task, await Task.WhenAny(linkStarted.Task, Task.Delay(TimeSpan.FromSeconds(10))));
        using var scope = fixture.CreateScope();
        Assert.Equal("stalledUP", scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Torrents.Single().ClientStatus);
    }

    [Fact]
    public async Task Status_StillDownloadingInQBittorrent_StaysDownloading_AndLeavesTheLinkJobAlone()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: QBittorrentWithHash1("downloading", 0.4));
        var linkRuns = 0;
        fixture.JobRegistry.Register("Link", _ =>
        {
            linkRuns++;
            return Task.CompletedTask;
        });
        await SeedTorrentAsync(fixture, libraryPath: null, clientStatus: null);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=1");

        Assert.Contains("status-downloading", html);
        Assert.Equal(0, linkRuns);
    }

    [Fact]
    public async Task Status_QBittorrentUnreachable_KeepsTheStoredStatus()
    {
        using var fixture = new FetchshelfWebAppFixture(ConfigWithQBittorrent, qbittorrentHandler: QBittorrentWithHash1(null, 0));
        await SeedTorrentAsync(fixture, libraryPath: null, clientStatus: null);
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/?handler=Status&mamId=1&round=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("status-downloading", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_Downloading_ShowsItAndAsksAgain()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedTorrentAsync(fixture, libraryPath: null, clientStatus: null);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=0");

        Assert.Contains("status-downloading", html);
        Assert.Contains("round=1&amp;handler=Status", html);
    }

    [Fact]
    public async Task Status_Selected_AsksAgainToo()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedSelectionAsync(fixture);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=0");

        Assert.Contains("status-selected", html);
        Assert.Contains("handler=Status", html);
    }

    [Fact]
    public async Task Status_Linked_ShowsItAndStopsAsking()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedTorrentAsync(fixture, libraryPath: "/library/Brandon Sanderson/The Way of Kings");
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Status&mamId=1&round=7");

        Assert.Contains("status-linked", html);
        Assert.DoesNotContain("hx-get", html);
    }

    [Fact]
    public async Task Status_AfterAnHourOfAsking_Stops()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await SeedTorrentAsync(fixture, libraryPath: null);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync($"/?handler=Status&mamId=1&round={Pages.SearchResultStatusViewModel.MaxRounds}");

        Assert.Contains("status-downloaded", html);
        Assert.DoesNotContain("hx-get", html);
    }

    [Fact]
    public async Task Status_TorrentFetchshelfDoesNotHave_IsEmptyAndStops()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/?handler=Status&mamId=99&round=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("status-badge", html);
        Assert.DoesNotContain("hx-get", html);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Results_OnlyRowsOnTheirWayIntoTheLibraryAsk(bool linked, bool asks)
    {
        const string json = """
            { "perpage": 100, "start": 0, "total": 1, "found": 1, "data": [ {
                "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}", "narrator_info": "{}",
                "series_info": "{}", "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "10 MiB", "tags": "",
                "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00", "seeders": 5, "leechers": 1,
                "times_completed": 1, "free": false, "personal_freeleech": false, "fl_vip": false, "vip": false,
                "vip_expire": 0, "dl": "dlhash1" } ] }
            """;
        using var fixture = new FetchshelfWebAppFixture(mamHandler: request => request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await SeedTorrentAsync(fixture, libraryPath: linked ? "/library/Brandon Sanderson/The Way of Kings" : null);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Equal(asks, html.Contains("round=1&amp;handler=Status", StringComparison.Ordinal));
    }
}
