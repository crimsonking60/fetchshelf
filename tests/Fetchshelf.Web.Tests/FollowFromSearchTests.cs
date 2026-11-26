using System.Net;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Tests;

/// <summary>Following a series or author from MAM's search results, with nothing of it in the library.</summary>
public partial class FollowPagesTests
{
    private const string ExpanseHitJson = """
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": 1, "title": "Leviathan Wakes", "author_info": "{\"1\":\"James S. A. Corey\"}",
                    "narrator_info": "{}", "series_info": "{\"3\":[\"The Expanse\",\"1\"]}",
                    "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "1.2 GiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 42,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dlhash1"
                }
            ]
        }
        """;

    private static FetchshelfWebAppFixture SearchFixture(string config = ConfigWithFollow) =>
        new(configToml: config, mamHandler: request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php") ? ExpanseHitJson : "{}"),
        });

    [Fact]
    public async Task SearchResultPage_HasFollowButtons_ForItsSeriesAndAuthors()
    {
        using var fixture = SearchFixture();

        var html = WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/search/1?Q=leviathan"));

        Assert.Contains("<div id=\"follow-buttons\">", html);
        Assert.Contains(">Follow The Expanse</button>", html);
        Assert.Contains(">Follow James S. A. Corey</button>", html);
        Assert.Contains("name=\"returnUrl\" value=\"/search/1?Q=leviathan\"", html);
    }

    [Fact]
    public async Task SearchResultDialog_FollowComesBackToThePageThatOpenedIt()
    {
        using var fixture = SearchFixture();
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/search/1?handler=Partial");
        request.Headers.Add("HX-Request", "true");
        request.Headers.Add("HX-Current-URL", "http://localhost/?Q=leviathan");

        var html = WebUtility.HtmlDecode(await (await client.SendAsync(request)).Content.ReadAsStringAsync());

        Assert.Contains("name=\"returnUrl\" value=\"/?Q=leviathan\"", html);
    }

    [Fact]
    public async Task SearchResultPage_WithoutAFollowSection_HasNoFollowButtons()
    {
        using var fixture = SearchFixture("mam_id = \"\"\n");

        var html = await fixture.CreateClient().GetStringAsync("/search/1");

        Assert.DoesNotContain("follow-buttons", html);
    }

    [Fact]
    public async Task SearchInTheSeriesField_OffersToFollowTheSeries_AsMamSpellsIt()
    {
        using var fixture = SearchFixture();
        using var client = fixture.CreateNoRedirectClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?handler=Results&Q=%22the+expanse%22&In=series"));

        Assert.Contains("id=\"search-follow\"", html);
        Assert.Contains(">Follow The Expanse</button>", html);
        Assert.Contains("name=\"kind\" value=\"Series\"", html);

        var response = await PostAsync(client, "/search/1", "/Following?handler=Follow", new()
        {
            ["kind"] = "Series",
            ["key"] = "expanse",
            ["name"] = "The Expanse",
            ["returnUrl"] = "/?Q=%22the%20expanse%22&In=series",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(FollowOf(fixture, FollowKind.Series, "expanse"));
        var after = WebUtility.HtmlDecode(await client.GetStringAsync("/?handler=Results&Q=%22the+expanse%22&In=series"));
        Assert.Contains(">Following The Expanse</a>", after);
    }

    [Theory]
    [InlineData("/?handler=Results&Q=expanse")] // title and author
    [InlineData("/?handler=Results&Q=expanse&In=series&In=title")]
    [InlineData("/?handler=Results&Q=%22the+churn%22&In=series")] // no result in that series
    public async Task OtherSearches_OfferNothingToFollow(string url)
    {
        using var fixture = SearchFixture();

        var html = await fixture.CreateClient().GetStringAsync(url);

        Assert.DoesNotContain("search-follow", html);
    }
}
