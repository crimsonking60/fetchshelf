using System.Net;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// With `genres = true`, the Search page's Fiction and Genre filters, which hide rows of the page in the browser like Hide
/// downloaded torrents and Show: every row carries its genres and main category. And a result's main category and genres
/// by MAM's names.
/// </summary>
public sealed class SearchGenreFilterTests
{
    // Three hits: nonfiction Historical and Military/War, fiction Historical, fiction Science Fiction and Romance. MAM
    // found 250, so there is a next page.
    private const string SearchJson = """
        {
            "perpage": 100, "start": 0, "total": 250, "found": 250,
            "data": [
                { "id": 1, "title": "Nonfiction History", "author_info": "{\"1\":\"Mary Beard\"}", "narrator_info": "{}", "series_info": "{}",
                  "mediatype": 2, "main_cat": 14, "maincat": 2, "category": 76, "categories": "[17, 63]", "language": 1, "browseflags": 0,
                  "filetype": "epub", "numfiles": 1, "size": "1 MiB", "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                  "seeders": 5, "leechers": 1, "times_completed": 1, "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dl1" },
                { "id": 2, "title": "Historical Novel", "author_info": "{\"2\":\"Hilary Mantel\"}", "narrator_info": "{}", "series_info": "{}",
                  "mediatype": 2, "main_cat": 14, "maincat": 1, "category": 102, "categories": "[17]", "language": 1, "browseflags": 0,
                  "filetype": "epub", "numfiles": 1, "size": "1 MiB", "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-02 00:00:00",
                  "seeders": 5, "leechers": 1, "times_completed": 1, "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dl2" },
                { "id": 3, "title": "Space Romance", "author_info": "{\"3\":\"Becky Chambers\"}", "narrator_info": "{}", "series_info": "{}",
                  "mediatype": 2, "main_cat": 14, "maincat": 1, "category": 69, "categories": "[45, 42]", "language": 1, "browseflags": 0,
                  "filetype": "epub", "numfiles": 1, "size": "1 MiB", "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-03 00:00:00",
                  "seeders": 5, "leechers": 1, "times_completed": 1, "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dl3" }
            ]
        }
        """;

    private static Func<HttpRequestMessage, HttpResponseMessage> Mam() => request =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php") ? SearchJson : "{}"),
        };

    private static FetchshelfWebAppFixture Fixture(bool genres) =>
        new($"mam_id = \"\"\ngenres = {(genres ? "true" : "false")}\n", mamHandler: Mam());

    private static string Results(string html) =>
        html.IndexOf("<table id=\"search-results\"", StringComparison.Ordinal) is var start and >= 0 ? html[start..] : string.Empty;

    [Fact]
    public async Task EveryRow_CarriesItsGenresAndMainCategory_ForTheFiltersInTheBrowser()
    {
        using var fixture = Fixture(genres: true);

        var html = await fixture.CreateClient().GetStringAsync("/?handler=Results&Q=history");

        Assert.Contains("data-genres=\"17 63\" data-fiction=\"Nonfiction\"", html);
        Assert.Contains("data-genres=\"17\" data-fiction=\"Fiction\"", html);
        Assert.Contains("data-genres=\"42 45\" data-fiction=\"Fiction\"", html);
    }

    [Fact]
    public async Task TheFilters_InTheResultsBox_AboveTheOthers_WithEveryMamGenre_AndAskMamNothing()
    {
        using var fixture = Fixture(genres: true);
        var client = fixture.CreateClient();

        var page = await client.GetStringAsync("/?Q=history");
        // The address doesn't filter: every result comes back, for the browser to hide.
        var results = await client.GetStringAsync("/?handler=Results&Q=history&Genre=none&Fiction=Fiction");

        var start = page.IndexOf("id=\"search-genre-filters\"", StringComparison.Ordinal);
        var filters = page[start..page.IndexOf("</div>", start, StringComparison.Ordinal)];
        Assert.True(page.IndexOf("id=\"search-filters\"", StringComparison.Ordinal) < start);
        Assert.True(start < page.IndexOf("id=\"hide-downloaded\"", StringComparison.Ordinal));
        Assert.True(filters.IndexOf("id=\"search-fiction\"", StringComparison.Ordinal) < filters.IndexOf("id=\"search-genre\"", StringComparison.Ordinal));
        Assert.DoesNotContain("name=", filters);
        Assert.Contains("<option value=\"63\">Military/War</option>", filters);
        Assert.Contains("<option value=\"26\">Erotica/Sexual Content</option>", filters);
        Assert.Contains("<option value=\"none\">No genre</option>", filters);
        Assert.DoesNotContain("value=\"Neither\"", filters);
        Assert.Contains("Space Romance", Results(results));
        Assert.Contains("Nonfiction History", Results(results));
    }

    [Fact]
    public async Task GenresOff_NoFilters()
    {
        using var fixture = Fixture(genres: false);

        var page = await fixture.CreateClient().GetStringAsync("/?Q=history");

        Assert.DoesNotContain("id=\"search-genre-filters\"", page);
    }

    [Fact]
    public async Task AResultsPage_ShowsItsMainCategoryAndGenres_ByMamsNames()
    {
        using var fixture = Fixture(genres: true);

        var html = WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/search/1?Q=history"));

        Assert.Contains("id=\"hit-main-category\"><span class=\"label\">Main category</span><span class=\"value\">Nonfiction</span>", html);
        Assert.Contains("id=\"hit-genres\"><span class=\"label\">Genres</span><span class=\"value\">Historical, Military/War</span>", html);
    }

}
