using System.Net;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// A search result's cover with `[search] covers = true`: from the cover sources by the result's ISBN, title and author,
/// never from MAM, and asked once an hour at most. With it off, none is shown or fetched.
/// </summary>
public sealed class SearchCoverTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    private const string SearchJson = """
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                { "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}", "narrator_info": "{}", "series_info": "{}",
                  "mediatype": 2, "main_cat": 14, "maincat": 1, "category": 63, "categories": "[]", "language": 1, "browseflags": 0,
                  "filetype": "epub", "numfiles": 1, "size": "1 MiB", "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                  "seeders": 5, "leechers": 1, "times_completed": 1, "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dl1",
                  "isbn": "9780765326355" }
            ]
        }
        """;

    private static Func<HttpRequestMessage, HttpResponseMessage> Mam(List<string> asked) => request =>
    {
        asked.Add(request.RequestUri!.AbsolutePath);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchJson) };
    };

    [Fact]
    public async Task CoversOn_TheDetailsShowACoverFromOpenLibrary_AskedOnce_AndNothingOfMamForIt()
    {
        var mam = new List<string>();
        var covers = new List<string>();
        using var fixture = new FetchshelfWebAppFixture(
            "mam_id = \"\"\n[search]\ncovers = true\n", mamHandler: Mam(mam),
            coversHandler: request =>
            {
                covers.Add(request.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) };
            });
        var client = fixture.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/search/1?Q=kings"));
        var start = page.IndexOf("class=\"hit-cover\" src=\"", StringComparison.Ordinal) + "class=\"hit-cover\" src=\"".Length;
        var address = page[start..page.IndexOf('"', start)];
        var mamCallsForThePage = mam.Count;
        var first = await client.GetAsync(address);
        var second = await client.GetAsync(address);

        Assert.Contains("handler=Cover", address);
        // A book no source has a cover for gets a 404, and the image goes rather than showing as broken.
        Assert.Contains("onerror=\"this.remove()\"", page);
        Assert.Contains("isbn=9780765326355", address);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/jpeg", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Jpeg, await second.Content.ReadAsByteArrayAsync());
        Assert.Equal(["https://covers.openlibrary.org/b/isbn/9780765326355-L.jpg?default=false"], covers);
        Assert.Equal(mamCallsForThePage, mam.Count);
    }

    [Fact]
    public async Task CoversOff_NoCover_AndTheHandlerFetchesNothing()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\n", mamHandler: Mam([]));
        var client = fixture.CreateClient();

        var page = await client.GetStringAsync("/search/1?Q=kings");
        var cover = await client.GetAsync("/search/1?handler=Cover&title=The%20Way%20of%20Kings&authors=Brandon%20Sanderson&isbn=9780765326355");

        Assert.DoesNotContain("hit-cover", page);
        Assert.Equal(HttpStatusCode.NotFound, cover.StatusCode);
    }
}
