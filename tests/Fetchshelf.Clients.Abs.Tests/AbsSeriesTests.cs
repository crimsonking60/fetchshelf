using System.Net;
using System.Text;

namespace Fetchshelf.Clients.Abs.Tests;

/// <summary>`GET /api/libraries/{id}/series`, in the shape Audiobookshelf 2.37 answers it.</summary>
public sealed class AbsSeriesTests
{
    private static AbsClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new AbsClientOptions { BaseUrl = "http://abs.local", Token = "t" });

    /// <summary>A book item as the series list returns it: minified, its place only in <c>seriesName</c>.</summary>
    private static string Book(string id, string title, string? seriesName, int tracks = 1, string? ebookFormat = null) => $$"""
        {
          "id": "{{id}}", "ino": "1", "libraryId": "lib1", "folderId": "f1", "path": "/books/{{id}}", "relPath": "{{id}}",
          "isFile": false, "mediaType": "book", "numFiles": 1, "size": 100,
          "media": {
            "id": "m{{id}}", "coverPath": null, "tags": [], "numTracks": {{tracks}}, "numAudioFiles": {{tracks}}, "numChapters": 0,
            "duration": 0, "size": 100{{(ebookFormat is null ? "" : $", \"ebookFormat\": \"{ebookFormat}\"")}},
            "metadata": {
              "title": "{{title}}", "titleIgnorePrefix": "{{title}}", "subtitle": null, "authorName": "Someone", "authorNameLF": "Someone",
              "narratorName": "", "seriesName": {{(seriesName is null ? "null" : $"\"{seriesName}\"")}}, "genres": [], "publishedYear": null,
              "publishedDate": null, "publisher": null, "description": null, "isbn": null, "asin": null, "language": null,
              "explicit": false, "abridged": false
            }
          }
        }
        """;

    private static string Series(string name, params string[] books) =>
        $$"""{ "id": "ser_{{name.Length}}", "name": "{{name}}", "nameIgnorePrefix": "{{name}}", "description": null, "addedAt": 1, "updatedAt": 1, "libraryId": "lib1", "books": [{{string.Join(",", books)}}] }""";

    private static string Page(int total, params string[] series) =>
        $$"""{ "results": [{{string.Join(",", series)}}], "total": {{total}}, "limit": 2, "page": 0, "sortDesc": false, "minified": false, "include": "" }""";

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListSeriesAsync_ReadsEachSeriesBooks_WithTheirPlaceAndFormats()
    {
        Uri? asked = null;
        var client = CreateClient(request =>
        {
            asked = request.RequestUri;
            return Json(Page(2,
                Series("Blandings Castle",
                    Book("b1", "Something Fresh", "Blandings Castle #1"),
                    Book("b2", "Lord Emsworth and Others", "Drones Club, Blandings Castle #5.5, Ukridge #3", tracks: 0, ebookFormat: "epub")),
                Series("Piers Anthony", Book("b3", "The Ring", "Piers Anthony", tracks: 0))));
        });

        var series = await client.ListSeriesAsync("lib1", pageSize: 2);

        Assert.Equal("/api/libraries/lib1/series", asked!.AbsolutePath);
        Assert.Equal("?limit=2&page=0&sort=name", asked.Query);
        Assert.Equal(["Blandings Castle", "Piers Anthony"], series.Select(s => s.Name));
        Assert.Equal(
            [new AbsSeriesBook("b1", "Something Fresh", "1", HasAudio: true, HasEbook: false),
             new AbsSeriesBook("b2", "Lord Emsworth and Others", "5.5", HasAudio: false, HasEbook: true)],
            series[0].Books);
        Assert.Equal(new AbsSeriesBook("b3", "The Ring", null, HasAudio: false, HasEbook: false), Assert.Single(series[1].Books));
    }

    [Fact]
    public async Task ListSeriesAsync_ReadsEveryPage()
    {
        var pages = new List<string>();
        var client = CreateClient(request =>
        {
            pages.Add(request.RequestUri!.Query);
            return request.RequestUri.Query.Contains("page=0", StringComparison.Ordinal)
                ? Json(Page(3, Series("A", Book("a", "A", "A #1")), Series("B", Book("b", "B", "B #1"))))
                : Json(Page(3, Series("C", Book("c", "C", "C #1"))));
        });

        var series = await client.ListSeriesAsync("lib1", pageSize: 2);

        Assert.Equal(["A", "B", "C"], series.Select(s => s.Name));
        Assert.Equal(["?limit=2&page=0&sort=name", "?limit=2&page=1&sort=name"], pages);
    }

    [Fact]
    public async Task ListSeriesAsync_EmptyLibrary_ReadsOnePage()
    {
        var calls = 0;
        var client = CreateClient(_ =>
        {
            calls++;
            return Json(Page(0));
        });

        Assert.Empty(await client.ListSeriesAsync("lib1"));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("Saga #1", "Saga", "1")]
    [InlineData("Saga", "Saga", null)]
    [InlineData("Blandings Castle #2, Psmith #4", "Psmith", "4")]
    [InlineData("Blandings Castle #2, Psmith #4", "Blandings Castle", "2")]
    [InlineData("As You Wake, Break the Shell #1", "As You Wake, Break the Shell", "1")]
    [InlineData("Known Space #9, Beowulf Shaeffer", "Beowulf Shaeffer", null)]
    [InlineData("2000 AD (1977) #2163-2212", "2000 AD (1977)", "2163-2212")]
    [InlineData("Doctor Who #The Krotons", "Doctor Who", "The Krotons")]
    [InlineData("Super Saga #3", "Saga", null)]
    [InlineData(null, "Saga", null)]
    public void Sequence_IsFoundByTheSeriesWholeName(string? joined, string name, string? expected) =>
        Assert.Equal(expected, AbsSeriesSequence.Of(joined, name));
}
