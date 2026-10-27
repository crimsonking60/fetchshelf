using System.Net;
using System.Text.RegularExpressions;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 10.3: the Search page, also the application's home page ("/").</summary>
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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"other\"", html);
        Assert.Contains("data-media-filter=\"Other\"", html);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Get_AudiobookOrPeriodicalAudiobookResult_RowTaggedWithAudiobookMediaType(int mediaTypeId)
    {
        var json = SearchResponseJson(large: false).Replace("\"mediatype\": 1", $"\"mediatype\": {mediaTypeId}");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("data-media-type=\"comic\"", html);
        Assert.Contains("data-media-filter=\"Comics\"", html);
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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("The Way of Kings", html);
        Assert.Contains("Brandon Sanderson", html);
        Assert.Contains("badge-freeleech", html);
        Assert.Contains("data-status=\"none\"", html);
    }

    [Fact]
    public async Task Get_WithoutQuery_ShowsNewestTorrentsFromMam()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("Newest on MaM", html);
        Assert.Contains("The Way of Kings", html);
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

        await client.GetAsync("/");

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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("wedge-highlighted", html);
    }

    [Fact]
    public async Task Get_Results_ShowMediaTypeColumn()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("<th>Media type</th>", html);
        Assert.Contains("<td>Audiobook</td>", html);
    }

    [Fact]
    public async Task Get_SizeAboveWedgeOverThreshold_HighlightsWedge()
    {
        using var fixture = new FetchshelfWebAppFixture(
            "mam_id = \"\"\n\n[search]\nwedge_over = \"1 GiB\"\n", mamHandler: MamHandler(SearchResponseJson(large: true)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

        Assert.Contains("status-selected", html);
        Assert.Contains(">Selected<", html);
        Assert.DoesNotContain("status-downloaded", html);
        Assert.DoesNotContain("status-linked", html);
        Assert.Contains("data-status=\"selected\"", html);
    }

    [Fact]
    public async Task Get_ResultAlreadyDownloadedNotLinked_ShowsDownloadedBadge()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: null);
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

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

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

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
        // data-media-type attribute, not via a MaM-side main_cat query, so every media type is
        // always fetched regardless of which button the page shows as selected.
        string? capturedBody = null;
        using var fixture = new FetchshelfWebAppFixture(mamHandler: CapturingMamHandler(SearchResponseJson(large: false), body => capturedBody = body));
        var client = fixture.CreateClient();

        await client.GetAsync("/?Q=kings");

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("\"main_cat\"", capturedBody);
    }

    [Fact]
    public async Task Get_AudiobookResult_RowTaggedWithAudiobookMediaType()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(SearchResponseJson(large: false)));
        var client = fixture.CreateClient();

        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();

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

        var html = await (await client.GetAsync("/?Q=radiance")).Content.ReadAsStringAsync();

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
        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();
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

    [Fact]
    public async Task Post_Download_NoPreferredFormat_CreatesNoSelectionAndExplainsWhy()
    {
        // Musicology torrent whose only file type (m4a) is not in the default music_types (flac, mp3, pdf).
        var json = SearchResponseJson(large: false)
            .Replace("\"mediatype\": 1", "\"mediatype\": 3")
            .Replace("\"filetype\": \"m4b\"", "\"filetype\": \"m4a\"");
        using var fixture = new FetchshelfWebAppFixture(mamHandler: MamHandler(json));
        var client = fixture.CreateClient();
        var html = await (await client.GetAsync("/?Q=kings")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

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
