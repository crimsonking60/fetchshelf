using System.Net;
using Fetchshelf.Web.Pages.Search;

namespace Fetchshelf.Web.Tests;

public class SearchHitPageTests
{
    private const string HitJson = """
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "narrator_info": "{\"2\":\"Michael Kramer\"}", "series_info": "{\"3\":[\"The Stormlight Archive\",\"1\"]}",
                    "isbn": "ASIN:B003P2WO5E", "description": "First line<br>Second &amp; last<script>alert(1)</script>",
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "1.2 GiB",
                    "tags": "epic fantasy", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 42,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dlhash1",
                    "lang_code": "ENG",
                    "thumbnail": "https://cdn.myanonamouse.net/covers/1.jpg",
                    "mediainfo": "{\"General\":{\"Duration\":\"45h 37m\",\"Format\":\"MPEG-4\"},\"Audio1\":{\"BitRate\":\"64 kb/s\",\"Channels\":2}}"
                }
            ]
        }
        """;

    private static Func<HttpRequestMessage, HttpResponseMessage> Mam(string json, List<string>? bodies = null) => request =>
    {
        if (request.RequestUri!.AbsolutePath.Contains("/covers/"))
        {
            var content = new ByteArrayContent([7, 7, 7]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        if (request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php"))
        {
            bodies?.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    };

    [Fact]
    public async Task Get_ShowsEverythingMamReturns_WithCoverAndMediaInfo()
    {
        var bodies = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(HitJson, bodies));

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/search/1?Q=kings"));

        Assert.Contains("\"thumbnail\":true", bodies.Single());
        Assert.Contains("\"mediaInfo\":true", bodies.Single());
        Assert.Contains("<h2>The Way of Kings</h2>", html);
        Assert.Contains("Michael Kramer", html);
        Assert.Contains("The Stormlight Archive #1", html);
        Assert.Contains("ASIN:B003P2WO5E", html);
        Assert.Contains("class=\"hit-cover\" src=\"/search/1?url=https%3A%2F%2Fcdn.myanonamouse.net%2Fcovers%2F1.jpg&handler=Cover\"", html);
        Assert.Contains("<h4>Audio1</h4>", html);
        Assert.Contains("45h 37m", html);
        Assert.Contains("<th>lang_code</th>", html);
        Assert.Matches(@"id=""back-to-search"" href=""/\?Q=kings""", html);
    }

    [Fact]
    public async Task GetPartial_ReturnsOnlyTheDetails_ForTheDialog()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(HitJson));

        var html = await fixture.CreateClient().GetStringAsync("/search/1?handler=Partial");

        Assert.Contains("<h2>The Way of Kings</h2>", html);
        Assert.Contains("id=\"open-on-mam\"", html);
        Assert.DoesNotContain("<html", html);
        Assert.DoesNotContain("back-to-search", html);
    }

    [Fact]
    public async Task SearchPage_HasTheDialogForTheDetails()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(HitJson));

        var html = await fixture.CreateClient().GetStringAsync("/");

        Assert.Contains("<dialog id=\"hit-dialog\"", html);
        Assert.Contains("id=\"hit-dialog-body\"", html);
    }

    [Fact]
    public async Task GetCover_FetchesTheImageFromMam()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(HitJson));

        var response = await fixture.CreateClient().GetAsync("/search/1?handler=Cover&url=" + Uri.EscapeDataString("https://cdn.myanonamouse.net/covers/1.jpg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal([7, 7, 7], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetCover_AddressOutsideMam_IsNotFetched()
    {
        var requested = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(mamHandler: request =>
        {
            requested.Add(request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var response = await fixture.CreateClient().GetAsync("/search/1?handler=Cover&url=" + Uri.EscapeDataString("http://192.168.1.1/admin"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(requested);
    }

    [Fact]
    public async Task Get_DescriptionIsShownAsText_NeverAsMarkup()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(HitJson));

        var html = await fixture.CreateClient().GetStringAsync("/search/1");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("class=\"hit-description\">First line&#xA;Second &amp; last</p>", html);
    }

    [Fact]
    public async Task Get_UnknownTorrent_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam("""{"perpage":100,"start":0,"total":0,"found":0,"data":[]}"""));

        var html = await fixture.CreateClient().GetStringAsync("/search/99");

        Assert.Contains("MAM returned no torrent with id 99.", html);
    }

    [Fact]
    public async Task SearchResults_LinkEachTitleToItsPage_KeepingTheSearch()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(HitJson));

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=kings"));

        Assert.Contains(
            "<a hx-get=\"/search/1?handler=Partial\" hx-target=\"#hit-dialog-body\" hx-swap=\"innerHTML\" href=\"/search/1?Q=kings\">The Way of Kings</a>", html);
    }

    [Theory]
    [InlineData("https://cdn.example/a.jpg", "https://cdn.example/a.jpg")]
    [InlineData("/covers/a.jpg", "https://mam.example/covers/a.jpg")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("a.jpg", null)]
    [InlineData("", null)]
    public void ThumbnailUrl_OnlyWebAddresses(string thumbnail, string? expected) =>
        Assert.Equal(expected, MamHitDetails.ThumbnailUrl(thumbnail, "https://mam.example/"));

    [Fact]
    public void MediaInfoSections_PutsLooseValuesFirst()
    {
        var sections = MamHitDetails.MediaInfoSections("""{"Version":3,"General":{"Format":"MP3"},"menu":{"extra":["00:00 One","00:10 Two"]}}""");

        Assert.Equal(["", "General", "menu"], sections.Select(s => s.Title));
        Assert.Equal(("Version", "3"), sections[0].Rows.Single());
        Assert.Equal(("extra", "00:00 One, 00:10 Two"), sections[2].Rows.Single());
    }

    [Fact]
    public void DescriptionText_DropsTagsScriptsAndExtraBlankLines()
    {
        var text = MamHitDetails.DescriptionText("<p>One</p><style>p{}</style><p>Two &lt;3</p><br><br><br><br>Three");

        Assert.Equal("One\nTwo <3\n\nThree", text);
    }
}
