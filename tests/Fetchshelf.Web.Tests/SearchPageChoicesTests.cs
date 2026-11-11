using System.Net;
using System.Text.RegularExpressions;
using Fetchshelf.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Search page's fields, sort, type and pages, and the category a download goes to.</summary>
public partial class SearchPageChoicesTests
{
    private const string ConfigWithLibrary = """
        mam_id = ""

        [[library]]
        name = "Audiobooks"
        category = "audiobooks"
        library_dir = "/library/audiobooks"
        """;

    /// <summary>A search answer with one result per id, each snatched or not, out of <paramref name="found"/>.</summary>
    private static string SearchJson(int found, params (long Id, bool Snatched)[] results)
    {
        var data = string.Join(",", results.Select(r => $$"""
            {
                "id": {{r.Id}}, "title": "Book {{r.Id}}", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "10 MiB",
                "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                "seeders": 5, "leechers": 1, "times_completed": 1,
                "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                "vip_expire": 0, "dl": "dlhash{{r.Id}}", "my_snatched": {{(r.Snatched ? 1 : 0)}}
            }
            """));
        return $$"""{ "perpage": 100, "start": 0, "total": {{found}}, "found": {{found}}, "data": [{{data}}] }""";
    }

    /// <summary>A MAM that answers every search with <paramref name="json"/>, and keeps the body of each search request.</summary>
    private static (Func<HttpRequestMessage, HttpResponseMessage> Handler, List<string> Searches) Mam(string json)
    {
        var searches = new List<string>();
        return (request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php"))
            {
                searches.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }, searches);
    }

    [Fact]
    public async Task Results_SendTheChosenFieldsSortTypeAndPage()
    {
        var (handler, searches) = Mam(SearchJson(0));
        using var fixture = new FetchshelfWebAppFixture(mamHandler: handler);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/?handler=Results&Q=expanse&In=series&In=narrator&Sort=seeders&Type=freeleech&PageNumber=3");

        var body = Assert.Single(searches);
        Assert.Contains("\"srchIn\":[\"series\",\"narrator\"]", body);
        Assert.Contains("\"sortType\":\"seedersDesc\"", body);
        Assert.Contains("\"searchType\":\"fl\"", body);
        Assert.Contains("\"startNumber\":200", body);
    }

    [Fact]
    public async Task Page_KeepsTheChoices_AndAsksForTheResultsWithThem()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(SearchJson(0)).Handler);
        using var client = fixture.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?Q=expanse&In=narrator&In=series&Sort=title&Type=bookmarks"));

        Assert.Contains("<option value=\"title\" selected=\"selected\">Title</option>", html);
        Assert.Contains("<option value=\"bookmarks\" selected=\"selected\">My bookmarks</option>", html);
        Assert.Contains("name=\"In\" value=\"series\" checked=\"checked\"", html);
        Assert.Contains("Search in: series, narrator", html);
        Assert.Contains("Matches for 'expanse' (My bookmarks)", html);
        var resultsUrl = ResultsUrlPattern().Match(html).Groups[1].Value;
        Assert.Contains("In=series&In=narrator", resultsUrl);
        Assert.Contains("Sort=title", resultsUrl);
        Assert.Contains("Type=bookmarks", resultsUrl);
    }

    [Fact]
    public async Task Results_FirstOfSeveralPages_CountTheResultsAndLinkOnlyToTheNext()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(SearchJson(250, (1, false), (2, false))).Handler);
        using var client = fixture.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?handler=Results&Q=kings&Sort=newest"));

        Assert.Contains("Results 1–2 of 250.", html);
        Assert.DoesNotContain("id=\"previous-page\"", html);
        var next = NextPagePattern().Match(html).Groups[1].Value;
        Assert.Contains("PageNumber=2", next);
        Assert.Contains("Sort=newest", next);
        Assert.Contains("Q=kings", next);
    }

    [Fact]
    public async Task Results_MiddlePage_LinksBothWays()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(SearchJson(250, (1, false))).Handler);
        using var client = fixture.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?handler=Results&Q=kings&PageNumber=2"));

        Assert.Contains("Results 101–101 of 250.", html);
        Assert.Contains("id=\"previous-page\"", html);
        Assert.Contains("id=\"next-page\"", html);
    }

    [Fact]
    public async Task Results_LastPage_LinksOnlyBack()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(SearchJson(250, (1, false))).Handler);
        using var client = fixture.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?handler=Results&Q=kings&PageNumber=3"));

        Assert.Contains("Results 201–201 of 250.", html);
        Assert.Contains("id=\"previous-page\"", html);
        Assert.DoesNotContain("id=\"next-page\"", html);
    }

    [Fact]
    public async Task Results_NotSnatched_LeavesOutWhatTheAccountSnatched_AndSaysHowMany()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: Mam(SearchJson(3, (1, false), (2, true), (3, true))).Handler);
        using var client = fixture.CreateClient();

        var all = await client.GetStringAsync("/?handler=Results&Q=kings");
        var notSnatched = await client.GetStringAsync("/?handler=Results&Q=kings&Type=not_snatched");

        Assert.Contains("Book 2", all);
        Assert.Contains(">Snatched</span>", all);
        Assert.Contains("Book 1", notSnatched);
        Assert.DoesNotContain("Book 2", notSnatched);
        Assert.DoesNotContain("Book 3", notSnatched);
        Assert.Contains("2 left out as snatched before.", notSnatched);
    }

    [Fact]
    public async Task Results_OfferTheLibrariesCategories()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithLibrary, mamHandler: Mam(SearchJson(1, (1, false))).Handler);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("<option value=\"audiobooks\">audiobooks (Audiobooks)</option>", html);
        Assert.Contains("hx-include=\"#download-category\"", html);
    }

    [Fact]
    public async Task Download_IntoALibrarysCategory_SelectsItWithThatCategory_AndRemembersIt()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithLibrary, mamHandler: Mam(SearchJson(1, (1, false))).Handler);
        using var client = fixture.CreateNoRedirectClient();

        var (body, response) = await DownloadAsync(client, "audiobooks");

        using var scope = fixture.CreateScope();
        var selected = Assert.Single(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents);
        Assert.Equal("audiobooks", selected.Category);
        Assert.Contains("Category: audiobooks.", body);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("fs-search-category=audiobooks", StringComparison.Ordinal));
        var page = await client.GetStringAsync("/?handler=Results&Q=kings");
        Assert.Contains("<option value=\"audiobooks\" selected=\"selected\">", page);
    }

    [Fact]
    public async Task Download_IntoACategoryNobodyUses_IsRefused()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: ConfigWithLibrary, mamHandler: Mam(SearchJson(1, (1, false))).Handler);
        using var client = fixture.CreateNoRedirectClient();

        var (body, _) = await DownloadAsync(client, "made-up");

        using var scope = fixture.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents);
        Assert.Contains("Not downloaded: made-up is neither a library's category nor one qBittorrent has.", body);
    }

    private static async Task<(string Body, HttpResponseMessage Response)> DownloadAsync(HttpClient client, string category)
    {
        var html = await client.GetStringAsync("/?handler=Results&Q=kings");
        var token = TokenPattern().Match(html).Groups[1].Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/?handler=Download")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["mamId"] = "1",
                ["category"] = category,
                ["__RequestVerificationToken"] = token,
            }),
        };
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);
        return (WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), response);
    }

    [GeneratedRegex(@"id=""search-results-panel"" hx-get=""([^""]*)""")]
    private static partial Regex ResultsUrlPattern();

    [GeneratedRegex(@"<a id=""next-page"" href=""([^""]*)""")]
    private static partial Regex NextPagePattern();

    [GeneratedRegex(@"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]*)""")]
    private static partial Regex TokenPattern();
}
