using System.Net;

namespace Fetchshelf.Web.Tests;

/// <summary>The Search page's metadata quality pill.</summary>
public class SearchMetadataQualityTests
{
    private static string SearchJson(string extraFields) => $$"""
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "series_info": "{}", "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "10 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 1,
                    "free": false, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dlhash1",
                    {{extraFields}}
                }
            ]
        }
        """;

    private static async Task<string> ResultsHtmlAsync(string json, string configToml = "mam_id = \"\"\n")
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: configToml, mamHandler: request => request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        using var client = fixture.CreateClient();
        return await client.GetStringAsync("/?handler=Results&Q=kings");
    }

    [Fact]
    public async Task CompleteMetadata_ShowsAGreenGoodPill()
    {
        var html = await ResultsHtmlAsync(SearchJson($$"""
            "narrator_info": "{\"2\":\"Michael Kramer\"}", "isbn": "9780765326355", "poster_type": "jpg",
            "description": "{{new string('x', 120)}}"
            """));

        Assert.Contains("<span class=\"quality-pill quality-good\">Good</span>", html);
        Assert.Contains("Author, file types, description, cover, genres and ISBN or ASIN are all there.", html);
    }

    [Fact]
    public async Task NoFileTypes_ShowsARedPoorPillSayingFetchshelfCantDownloadIt()
    {
        var json = SearchJson($$"""
            "narrator_info": "{\"2\":\"Michael Kramer\"}", "isbn": "9780765326355", "poster_type": "jpg",
            "description": "{{new string('x', 120)}}"
            """).Replace("\"filetype\": \"m4b\"", "\"filetype\": \"\"");

        var html = await ResultsHtmlAsync(json);

        Assert.Contains("<span class=\"quality-pill quality-poor\">Poor</span>", html);
        Assert.Contains("Missing: file types (Fetchshelf can&#x27;t download it without them).", html);
    }

    [Fact]
    public async Task SparseMetadata_ShowsARedPoorPillSayingWhatIsMissing()
    {
        var html = await ResultsHtmlAsync(SearchJson("""
            "narrator_info": "{}", "isbn": null, "description": null
            """));

        Assert.Contains("<span class=\"quality-pill quality-poor\">Poor</span>", html);
        Assert.Contains("Missing: description, cover, narrator, ISBN or ASIN.", html);
    }

    [Fact]
    public async Task ResultsTable_HasAMetadataColumn()
    {
        var html = await ResultsHtmlAsync(SearchJson("""
            "narrator_info": "{}", "isbn": null, "description": null
            """));

        Assert.Contains("<th>Metadata</th>", html);
    }

    private const string Sparse = """
        "narrator_info": "{}", "isbn": null, "description": null
        """;

    [Fact]
    public async Task WantedFileType_OffersTheDownloadButtons()
    {
        var html = await ResultsHtmlAsync(SearchJson(Sparse));

        Assert.Contains(">Download</button>", html);
        Assert.DoesNotContain("Can't download", html);
    }

    [Fact]
    public async Task NoFileTypes_ShowsWhyInsteadOfDownloadButtons()
    {
        var html = await ResultsHtmlAsync(SearchJson(Sparse).Replace("\"filetype\": \"m4b\"", "\"filetype\": \"\""));

        Assert.Contains("Can't download", html);
        Assert.Contains("MAM lists no file types for this torrent", html);
        Assert.DoesNotContain(">Download</button>", html);
        Assert.DoesNotContain("Wedge\u00a0download", html);
    }

    [Fact]
    public async Task NoWantedFileType_ShowsWhyInsteadOfDownloadButtons()
    {
        var html = await ResultsHtmlAsync(
            SearchJson(Sparse).Replace("\"filetype\": \"m4b\"", "\"filetype\": \"mp3\""),
            "mam_id = \"\"\naudio_types = [\"m4b\"]\n");

        Assert.Contains("None of its file types (mp3) is in your audio_types setting (m4b)", html);
        Assert.DoesNotContain(">Download</button>", html);
    }
}
