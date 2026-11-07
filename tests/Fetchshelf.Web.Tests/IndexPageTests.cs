using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Search page, also the application's home page ("/").</summary>
public partial class IndexPageTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Get_MusicologyOrRadioResult_RowTaggedWithOtherMediaType(int mediaTypeId)
    {
        var json = SearchResponseJson(large: false).Replace("\"mediatype\": 1", $"\"mediatype\": {mediaTypeId}");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"other\"", html);
        Assert.Contains("data-media-filter=\"Other\"", await client.GetStringAsync("/"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Get_AudiobookOrPeriodicalAudiobookResult_RowTaggedWithAudiobookMediaType(int mediaTypeId)
    {
        var json = SearchResponseJson(large: false).Replace("\"mediatype\": 1", $"\"mediatype\": {mediaTypeId}");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"audiobook\"", html);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public async Task Get_EbookOrPeriodicalEbookResult_RowTaggedWithEbookMediaType(int mediaTypeId)
    {
        var json = SearchResponseJson(large: false).Replace("\"mediatype\": 1", $"\"mediatype\": {mediaTypeId}");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"ebook\"", html);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Get_MangaOrComicResult_RowTaggedWithComicMediaType(int mediaTypeId)
    {
        var json = SearchResponseJson(large: false).Replace("\"mediatype\": 1", $"\"mediatype\": {mediaTypeId}");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"comic\"", html);
        Assert.Contains("data-media-filter=\"Comics\"", await client.GetStringAsync("/"));
    }

    private static string SearchResponseJson(bool large) => $$"""
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1,
                    "size": "{{(large ? "2 GiB" : "10 MiB")}}",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 1,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dlhash1"
                }
            ]
        }
        """;

    private const string ConfigWithQBittorrent = """
        mam_id = ""

        [[qbittorrent]]
        url = "http://qbittorrent.test"
        username = "admin"
        password = "adminadmin"
        """;

    /// <summary>A qBittorrent that has the given torrents; null makes every request fail.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> QBittorrentWith(params QBittorrentTorrent[]? torrents) => request =>
    {
        if (torrents is null)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (request.RequestUri!.AbsolutePath.Contains("auth/login"))
        {
            var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
            login.Headers.Add("Set-Cookie", "SID=abc; path=/");
            return login;
        }

        return request.RequestUri.AbsolutePath.Contains("torrents/info")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) }
            : new HttpResponseMessage(HttpStatusCode.OK);
    };

    private static QBittorrentTorrent InClient(string hash, string comment, string category) =>
        new() { Hash = hash, Name = "Some torrent", SavePath = "/downloads", State = "uploading", Progress = 1.0, Comment = comment, Category = category };

    [Fact]
    public async Task GetResults_TorrentInQBittorrent_ShowsItWithItsCategory()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hashx", "MID=1,UID=9", "mouse")));

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("qBittorrent: mouse", html);
    }

    [Fact]
    public async Task GetResults_KnownTorrentWithoutMamComment_IsFoundByItsHash()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hash1", "", "")));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata() with { MamId = 1 }, libraryPath: "/library/x");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Torrents.Single().MamId = 1;
            await dbContext.SaveChangesAsync();
        }

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("qBittorrent: no category", html);
    }

    [Fact]
    public async Task GetResults_LinkedTorrentQBittorrentNoLongerHas_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("other", "MID=99", "")));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: "/library/x");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Torrents.Single().MamId = 1;
            await dbContext.SaveChangesAsync();
        }

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("status-linked", html);
        Assert.Contains("Not in qBittorrent", html);
    }

    [Theory]
    [InlineData(true, ">Download</button>", "Wedge&#xA0;download</button>")]
    [InlineData(false, ">Select</button>", "Wedge&#xA0;select</button>")]
    public async Task GetResults_ButtonsSayWhatHappens_DownloadOrOnlySelect(bool downloadAfterSelection, string plain, string wedge)
    {
        using var fixture = new FetchshelfWebAppFixture(
            $"mam_id = \"\"\ndownload_after_selection = {(downloadAfterSelection ? "true" : "false")}\n",
            mamHandler: MamHandler(SearchResponseJson(large: false)));

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains(plain, html);
        Assert.Contains(wedge, html);
    }

    [Fact]
    public async Task GetResults_TorrentInQBittorrent_OffersImportInsteadOfDownload()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hashx", "MID=1,UID=9", "mouse")));

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains(">Import</button>", html);
        Assert.DoesNotContain(">Download</button>", html);
        Assert.DoesNotContain("Wedge&#xA0;download", html);
    }

    [Fact]
    public async Task GetResults_TorrentFetchshelfHasInQBittorrent_OffersNeither()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hash1", "MID=1", "audiobooks")));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: "/library/x");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.Torrents.Single().MamId = 1;
            await dbContext.SaveChangesAsync();
        }

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.DoesNotContain(">Import</button>", html);
        Assert.DoesNotContain(">Download</button>", html);
    }

    [Fact]
    public async Task Post_Import_RecordsTheTorrentWithItsHashAndCategory()
    {
        var config = ConfigWithQBittorrent + """

            [[library]]
            library_dir = "/library/books"
            category = "mouse"
            """;
        using var fixture = new FetchshelfWebAppFixture(
            config, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hashx", "MID=1,UID=9", "mouse")));
        using var client = fixture.CreateNoRedirectClient();
        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Import")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mamId"] = "1",
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
            }),
        };
        request.Headers.Add("HX-Request", "true");
        var body = System.Net.WebUtility.HtmlDecode(await (await client.SendAsync(request)).Content.ReadAsStringAsync());

        Assert.Contains("Imported \"The Way of Kings\"; the next Link run links it.", body);
        Assert.Contains("status-downloaded", body);
        Assert.DoesNotContain(">Import</button>", body);
        using var scope = fixture.CreateScope();
        var torrent = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Torrents.Single();
        Assert.Equal("hashx", torrent.Id);
        Assert.Equal(1, torrent.MamId);
        Assert.Equal("mouse", torrent.QBittorrentCategory);
        Assert.Null(torrent.LibraryPath);
    }

    [Fact]
    public async Task Post_Import_CategoryNoLibraryUses_SaysHowToGetItLinked()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hashx", "MID=1,UID=9", "mouse")));
        using var client = fixture.CreateNoRedirectClient();
        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Import")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mamId"] = "1",
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
            }),
        };
        request.Headers.Add("HX-Request", "true");
        var body = System.Net.WebUtility.HtmlDecode(await (await client.SendAsync(request)).Content.ReadAsStringAsync());

        Assert.Contains("None of your libraries uses its qBittorrent category (mouse).", body);
        Assert.Contains("Set category", body);
    }

    [Fact]
    public async Task GetResults_QBittorrentUnreachable_StillShowsTheResults_WithoutAQBittorrentMark()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)), qbittorrentHandler: QBittorrentWith(null));

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("The Way of Kings", html);
        Assert.DoesNotContain("qBittorrent:", html);
        Assert.DoesNotContain("Not in qBittorrent", html);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> MamHandler(string searchJson) => request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("loadSearchJSONbasic.php"))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchJson) };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    };

    [Fact]
    public async Task Get_WithQuery_ListsResultWithCostBadge()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("The Way of Kings", html);
        Assert.Contains("Brandon Sanderson", html);
        Assert.Contains("badge-freeleech", html);
        Assert.Contains("data-status=\"none\"", html);
    }

    [Fact]
    public async Task Get_ResultWithManyAuthors_ShowsShortenedListWithFullListInTheBalloon()
    {
        var names = Enumerable.Range(1, 30).Select(i => $"Contributor {i:00}").ToList();
        // author_info is a JSON object serialised into a JSON string, hence the escaped quotes.
        var authorInfo = "{" + string.Join(",", names.Select((name, i) => $"\\\"{i + 1}\\\":\\\"{name}\\\"")) + "}";
        var json = SearchResponseJson(large: false).Replace("{\\\"1\\\":\\\"Brandon Sanderson\\\"}", authorInfo);
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("... more", html);
        Assert.Contains($"<span class=\"tip\" role=\"tooltip\">{string.Join(", ", names)}</span>", html);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> TimingOutMamHandler() =>
        _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.", new TimeoutException());

    [Fact]
    public async Task GetResults_MamTimesOut_ShowsATimeoutMessageInTheUi()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: TimingOutMamHandler());
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/?handler=Results&Q=kings");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"search-error\"", html);
        Assert.Contains("MAM did not answer within 30 seconds", html);
        Assert.Contains("Try again", html);
    }

    /// <summary>Answers the first search (so the results page renders with its forms), then times out on every later one.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> MamThatTimesOutAfterTheFirstSearch()
    {
        var searches = 0;
        var working = MamHandler(SearchResponseJson(large: false));
        return request =>
            request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php") && Interlocked.Increment(ref searches) > 1
                ? TimingOutMamHandler()(request)
                : working(request);
    }

    private static async Task<HttpResponseMessage> PostDownloadAsync(HttpClient client, string token, bool fromHtmx)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Download")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["mamId"] = "1", ["__RequestVerificationToken"] = token }),
        };
        if (fromHtmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Post_Download_FromHtmx_MamTimesOut_ReturnsAnErrorMessageAndNoRow()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamThatTimesOutAfterTheFirstSearch());
        using var client = fixture.CreateNoRedirectClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/?handler=Results&Q=kings"));

        var response = await PostDownloadAsync(client, token, fromHtmx: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // htmx only swaps successful responses
        Assert.Contains("class=\"notice error\"", body);
        Assert.Contains("Download failed: MAM did not answer within 30 seconds", body);
        Assert.DoesNotContain("<template>", body);
    }

    [Fact]
    public async Task Post_Download_WithoutHtmx_MamTimesOut_ShowsTheErrorAfterTheRedirect()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamThatTimesOutAfterTheFirstSearch());
        using var client = fixture.CreateClient();
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/?handler=Results&Q=kings"));

        var landingPage = await (await PostDownloadAsync(client, token, fromHtmx: false)).Content.ReadAsStringAsync();

        Assert.Contains("Download failed: MAM did not answer within 30 seconds", landingPage);
    }

    [Fact]
    public async Task Fixture_WithoutAMamHandler_FailsLoudlyInsteadOfReachingTheRealMam()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        // The fixture's guard throws a plain exception, which the page reports without its text.
        Assert.Contains("Something went wrong while talking to MAM. The log has the details.", html);
    }

    [Fact]
    public async Task Get_PageShell_DoesNotWaitOnMam_AndLoadsResultsThroughHtmx()
    {
        var mamCalls = 0;
        Func<HttpRequestMessage, HttpResponseMessage> handler = request =>
        {
            Interlocked.Increment(ref mamCalls);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchResponseJson(large: false)) };
        };
        using var fixture = new FetchshelfWebAppFixture(mamHandler: handler);
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?Q=kings");

        Assert.Equal(0, mamCalls);
        Assert.DoesNotContain("The Way of Kings", html);
        var hxGet = Regex.Match(html, "id=\"search-results-panel\" hx-get=\"([^\"]*)\"").Groups[1].Value;
        Assert.Contains("handler=Results", hxGet);
        Assert.Contains("Q=kings", hxGet);
        Assert.Contains("hx-trigger=\"load\"", html);
        Assert.Contains("id=\"search-loading\"", html);
    }

    [Fact]
    public async Task GetResults_MamRejectsTheSession_RendersAnErrorWithRetryInsteadOfFailing()
    {
        Func<HttpRequestMessage, HttpResponseMessage> handler = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        using var fixture = new FetchshelfWebAppFixture(mamHandler: handler);
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/?handler=Results&Q=kings");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // htmx only swaps successful responses
        Assert.Contains("id=\"search-error\"", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("id=\"search-results\"", html);
    }

    [Fact]
    public async Task Get_WithoutQuery_ShowsNewestTorrentsFromMam()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var shell = await client.GetStringAsync("/");
        var results = await client.GetStringAsync("/?handler=Results");

        Assert.Contains("Newest on MAM", shell);
        Assert.Contains("The Way of Kings", results);
    }

    [Fact]
    public async Task Get_WithoutQuery_SendsNoTextOrSearchFieldsToMam()
    {
        string? capturedBody = null;
        Func<HttpRequestMessage, HttpResponseMessage> handler = request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php"))
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchResponseJson(large: false)) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        };
        using var fixture = new FetchshelfWebAppFixture(mamHandler: handler);
        var client = fixture.CreateClient();

        await client.GetAsync("/?handler=Results");

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("\"text\"", capturedBody);
        Assert.DoesNotContain("\"srchIn\"", capturedBody);
    }

    [Fact]
    public async Task Get_SizeBelowWedgeOverThreshold_DoesNotHighlightWedge()
    {
        using var fixture = new FetchshelfWebAppFixture(
            "mam_id = \"\"\n\n[search]\nwedge_over = \"1 GiB\"\n", mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("wedge-highlighted", html);
    }

    [Fact]
    public async Task Get_Results_ShowMediaTypeColumn()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("<th>Media type</th>", html);
        Assert.Contains("<td>Audiobook</td>", html);
    }

    [Fact]
    public async Task Get_SizeAboveWedgeOverThreshold_HighlightsWedge()
    {
        using var fixture = new FetchshelfWebAppFixture(
            "mam_id = \"\"\n\n[search]\nwedge_over = \"1 GiB\"\n", mamHandler: MamHandler(SearchResponseJson(large: true)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("wedge-highlighted", html);
    }

    [Fact]
    public async Task Get_ResultAlreadySelected_ShowsSelectedBadge()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            dbContext.SelectedTorrents.Add(new SelectedTorrentEntity
            {
                MamId = 1,
                DownloadLink = "dlhash1",
                CostDecision = "Ratio",
                MetadataJson = "{}",
                NormalizedTitleKey = "the-way-of-kings",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await dbContext.SaveChangesAsync();
        }
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("status-selected", html);
        Assert.Contains(">Selected<", html);
        Assert.DoesNotContain("status-downloaded", html);
        Assert.DoesNotContain("status-linked", html);
        Assert.Contains("data-status=\"selected\"", html);
    }

    [Fact]
    public async Task Get_ResultFinishedNotLinked_ShowsDownloadedBadge()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: null, clientStatus: "stalledUP");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("status-downloaded", html);
        Assert.Contains(">Downloaded<", html);
        Assert.DoesNotContain("status-selected", html);
        Assert.DoesNotContain("status-linked", html);
        Assert.Contains("data-status=\"downloaded\"", html);
    }

    [Fact]
    public async Task Get_ResultAlreadyLinked_ShowsLinkedBadge()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: "/library/the-way-of-kings");
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("status-linked", html);
        Assert.Contains(">Linked<", html);
        Assert.DoesNotContain("status-selected", html);
        Assert.DoesNotContain("status-downloaded", html);
        Assert.Contains("data-status=\"linked\"", html);
    }

    [Fact]
    public async Task Get_Search_NeverSendsMainCategoryFilterToMam()
    {
        // The Ebooks/Audiobooks "Show" filter is applied browser-side against each row's
        // data-media-type attribute, not via a MAM-side main_cat query, so every media type is
        // always fetched regardless of which button the page shows as selected.
        string? capturedBody = null;
        using var fixture = new FetchshelfWebAppFixture(mamHandler: CapturingMamHandler(SearchResponseJson(large: false), body => capturedBody = body));
        var client = fixture.CreateClient();

        await client.GetAsync("/?handler=Results&Q=kings");

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("\"main_cat\"", capturedBody);
    }

    [Fact]
    public async Task Get_AudiobookResult_RowTaggedWithAudiobookMediaType()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"audiobook\"", html);
    }

    [Fact]
    public async Task Get_EbookResultWithNoMediaTypeField_FallsBackToOldMainCategoryForEbookMediaType()
    {
        const string EbookResponseJson = """
            {
                "perpage": 100, "start": 0, "total": 1, "found": 1,
                "data": [
                    {
                        "id": 2, "title": "Words of Radiance", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                        "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                        "main_cat": 14, "maincat": 1, "category": 1, "categories": "[14]",
                        "language": 1, "browseflags": 0, "filetype": "epub", "numfiles": 1,
                        "size": "2 MiB",
                        "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                        "seeders": 5, "leechers": 1, "times_completed": 1,
                        "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                        "vip_expire": 0, "dl": "dlhash2"
                    }
                ]
            }
            """;
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(EbookResponseJson));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=radiance")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"ebook\"", html);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> CapturingMamHandler(
        string searchJson, Action<string> captureBody) => request =>
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("loadSearchJSONbasic.php"))
        {
            captureBody(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(searchJson) };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    };

    [Fact]
    public async Task Post_Download_CreatesSelection()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mamId"] = "1",
            ["__RequestVerificationToken"] = token,
        });
        await client.PostAsync("/?handler=Download", form);

        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        var selected = Assert.Single(dbContext.SelectedTorrents);
        Assert.Equal(1, selected.MamId);
        Assert.Equal("Manual search", selected.GrabberName);
    }

    /// <summary>Clicks Download on the one search result through htmx and returns the decoded answer.</summary>
    private static async Task<string> ClickDownloadAsync(FetchshelfWebAppFixture fixture)
    {
        using var client = fixture.CreateNoRedirectClient();
        var html = await client.GetStringAsync("/?handler=Results&Q=kings");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Download")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mamId"] = "1",
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
            }),
        };
        request.Headers.Add("HX-Request", "true");
        return System.Net.WebUtility.HtmlDecode(await (await client.SendAsync(request)).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null, "status-downloading", ">Downloading<")]
    [InlineData("downloading", "status-downloading", ">Downloading<")]
    [InlineData("stalledDL", "status-downloading", ">Downloading<")]
    [InlineData("uploading", "status-downloaded", ">Downloaded<")]
    [InlineData("stoppedUP", "status-downloaded", ">Downloaded<")]
    public async Task Get_ResultNotLinked_IsDownloadingUntilQBittorrentHasFinishedIt(string? clientStatus, string badgeClass, string label)
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: null, clientStatus: clientStatus);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains(badgeClass, html);
        Assert.Contains(label, html);
    }

    [Fact]
    public async Task Get_MetadataOnlyRecord_IsDownloadedNotDownloading()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "mam:1", libraryPath: null);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("status-downloaded", html);
        Assert.DoesNotContain("status-downloading", html);
    }

    [Fact]
    public async Task Post_Download_AnswersAtOnceWithSelected_AndStartsTheDownloadJob()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        fixture.JobRegistry.Register("Download", async _ =>
        {
            started.TrySetResult();
            await release.Task; // a slow Download job must not hold up the answer
        });

        var body = await ClickDownloadAsync(fixture);
        release.TrySetResult();

        Assert.Contains("Selected \"The Way of Kings\"; the Download job is starting it now.", body);
        Assert.Contains("status-selected", body);
        Assert.Contains("handler=Status", body); // the badge takes it from there
        Assert.Same(started.Task, await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10))));
    }

    [Fact]
    public async Task GetResults_StoredAsDownloadingButFinishedInQBittorrent_ShowsDownloaded()
    {
        using var fixture = new FetchshelfWebAppFixture(
            ConfigWithQBittorrent, mamHandler: MamHandler(SearchResponseJson(large: false)),
            qbittorrentHandler: QBittorrentWith(InClient("hash1", "", "audiobooks")));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: null);

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("status-downloaded", html);
        Assert.DoesNotContain("status-downloading", html);
    }

    [Fact]
    public async Task Page_ResultsPanelDoesNotPassItsOverlayToButtonsAndStatusChecks()
    {
        using var fixture = new FetchshelfWebAppFixture();

        var html = await fixture.CreateClient().GetStringAsync("/");

        Assert.Contains("hx-indicator=\"#search-loading\" hx-disinherit=\"hx-indicator\"", html);
    }

    [Fact]
    public async Task Post_Download_DownloadAfterSelectionOff_LeavesItForTheScheduledRun()
    {
        using var fixture = new FetchshelfWebAppFixture(
            "mam_id = \"\"\ndownload_after_selection = false\n", mamHandler: MamHandler(SearchResponseJson(large: false)));
        var ran = false;
        fixture.JobRegistry.Register("Download", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        var body = await ClickDownloadAsync(fixture);

        Assert.Contains("it starts at the next Download run", body);
        Assert.False(ran);
    }

    [Fact]
    public async Task Post_Download_FromHtmx_ReturnsMessageAndRefreshedRowInsteadOfRedirecting()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        using var client = fixture.CreateNoRedirectClient();
        var html = await client.GetStringAsync("/?handler=Results&Q=kings");
        Assert.DoesNotContain("status-selected", html);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Download")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mamId"] = "1",
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
            }),
        };
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"download-message\"", body);
        Assert.Contains("Selected", body);
        // The row comes back as an out-of-band swap, now showing the Selected status.
        var template = Regex.Match(body, "<template>(.*)</template>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("id=\"result-1\"", template);
        Assert.Contains("hx-swap-oob=\"true\"", template);
        Assert.Contains("status-selected", template);
        Assert.Contains("data-status=\"selected\"", template);

        using var scope = fixture.CreateScope();
        Assert.Single(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents);
    }

    [Fact]
    public async Task Post_Download_FromHtmx_DoesNotLeaveAMessageForTheNextPageLoad()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        using var client = fixture.CreateNoRedirectClient();
        var html = await client.GetStringAsync("/?handler=Results&Q=kings");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Download")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mamId"] = "1",
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
            }),
        };
        request.Headers.Add("HX-Request", "true");
        await client.SendAsync(request);

        var nextShell = await client.GetStringAsync("/?Q=kings");

        Assert.DoesNotContain("id=\"download-message\"", nextShell);
    }

    [Fact]
    public async Task Get_ResultRows_PostDownloadsThroughHtmxButKeepTheFormAction()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("hx-post=\"/?handler=Download\"", html);
        Assert.Contains("hx-target=\"#download-message-area\"", html);
        Assert.Contains("action=\"/?handler=Download\"", html); // works without htmx too
        Assert.Contains("id=\"result-1\"", html);
    }

    [Fact]
    public async Task Post_Download_NoPreferredFormat_CreatesNoSelectionAndExplainsWhy()
    {
        // Musicology torrent whose only file type (m4a) is not in the default music_types (flac, mp3, pdf).
        var json = SearchResponseJson(large: false)
            .Replace("\"mediatype\": 1", "\"mediatype\": 3")
            .Replace("\"filetype\": \"m4b\"", "\"filetype\": \"m4a\"");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/?handler=Results&Q=kings")).Content.ReadAsStringAsync();

        // The row offers no download buttons for it, and says why.
        Assert.DoesNotContain("handler=Download", html);
        Assert.Contains("None of its file types (m4a) is in your music_types setting", html);

        // A request made anyway, from a page loaded before or by hand, is still refused. The row has no form, so the
        // antiforgery token comes from another page; it is valid for the whole session.
        var token = ExtractAntiforgeryToken(await client.GetStringAsync("/torrents"));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["mamId"] = "1",
            ["__RequestVerificationToken"] = token,
        });
        // The test client follows the redirect, so this is the Search page the user lands on.
        var afterRedirect = await (await client.PostAsync("/?handler=Download", form)).Content.ReadAsStringAsync();

        using var scope = fixture.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents);
        Assert.Contains("music_types", afterRedirect);
        Assert.Contains("m4a", afterRedirect);
    }

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*>")]
    private static partial Regex AntiforgeryInputPattern();

    [GeneratedRegex(@"value=""([^""]*)""")]
    private static partial Regex ValueAttributePattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var inputMatch = AntiforgeryInputPattern().Match(html);
        var valueMatch = ValueAttributePattern().Match(inputMatch.Value);
        return valueMatch.Groups[1].Value;
    }
}
