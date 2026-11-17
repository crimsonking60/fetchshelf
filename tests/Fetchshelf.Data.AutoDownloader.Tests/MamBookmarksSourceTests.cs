using System.Net;
using Fetchshelf.Clients.Mam;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public sealed class MamBookmarksSourceTests
{
    private static string Record(long mamId, string title, string author = "Frank Herbert", string seriesInfo = "{}", bool ebook = false, int category = 0) => $$"""
        {
            "id": {{mamId}}, "title": "{{title}}", "author_info": "{\"1\":\"{{author}}\"}",
            "narrator_info": "{}", "series_info": "{{seriesInfo.Replace("\"", "\\\"", StringComparison.Ordinal)}}", "isbn": null, "description": null,
            "mediatype": {{(ebook ? 2 : 1)}}, "main_cat": {{(ebook ? 14 : 13)}}, "maincat": 1,
            "category": {{(category != 0 ? category : ebook ? 60 : 39)}}, "categories": "[]",
            "language": 1, "browseflags": 0, "filetype": "{{(ebook ? "epub" : "m4b")}}", "numfiles": 1, "size": "100 MiB",
            "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
            "seeders": 5, "leechers": 1, "times_completed": 1,
            "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false, "vip_expire": 0, "dl": "dl{{mamId}}"
        }
        """;

    private static string Page(int found, params string[] records) =>
        $$"""{"perpage":100,"start":0,"total":{{found}},"found":{{found}},"data":[{{string.Join(",", records)}}]}""";

    /// <summary>A source over a MAM that answers each search with <paramref name="answer"/>, given the request body.</summary>
    private static (MamBookmarksSource Source, List<string> Searches, MamAccessGate Gate) Create(Func<string, string> answer)
    {
        var searches = new List<string>();
        var gate = new MamAccessGate();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            searches.Add(body);
            Assert.Equal("List MAM bookmarks", gate.Owner);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer(body)) };
        });
        var client = new MamClient(new HttpClient(handler), new MamClientOptions { MamId = "cookie" }, delay: (_, _) => Task.CompletedTask);
        return (new MamBookmarksSource(client, gate, "MAM bookmarks", delay: (_, _) => Task.CompletedTask), searches, gate);
    }

    [Fact]
    public async Task GetBooksAsync_ReadsTheBookmarks_UnderTheGate_AsBooksKnownByTheirMamId()
    {
        var (source, searches, gate) = Create(_ => Page(2,
            Record(11, "Dune", seriesInfo: """{"333":["Dune","1"]}"""),
            Record(12, "Hyperion", "Dan Simmons", ebook: true)));

        var books = await source.GetBooksAsync();

        var search = Assert.Single(searches);
        Assert.Contains("\"searchIn\":\"bookmarks\"", search);
        Assert.Null(gate.Owner);
        Assert.Equal(["11", "12"], books.Select(b => b.ExternalId));
        Assert.Equal(("Dune", "Dune", 1.0), (books[0].Title, books[0].SeriesName, books[0].SeriesNumber));
        Assert.Equal(["Frank Herbert"], books[0].Authors);
        Assert.Null(books[0].CoverUrl);
        Assert.Equal(12, source.KnownTorrent("12")?.MamId);
        Assert.Equal("dl12", source.KnownTorrent("12")?.DownloadHash);
        Assert.Null(source.KnownTorrent("13"));
    }

    [Fact]
    public async Task GetBooksAsync_FullPage_ReadsTheNextOne()
    {
        var first = Enumerable.Range(1, 100).Select(i => Record(i, $"Book {i}")).ToArray();
        var (source, searches, _) = Create(body => body.Contains("\"startNumber\":100", StringComparison.Ordinal)
            ? Page(101, Record(101, "Book 101"))
            : Page(101, first));

        var books = await source.GetBooksAsync();

        Assert.Equal(2, searches.Count);
        Assert.Equal(101, books.Count);
        Assert.Equal("101", books[^1].ExternalId);
    }

    [Fact]
    public async Task GetBooksAsync_NoBookmarks_ReadsOnePage()
    {
        var (source, searches, _) = Create(_ => Page(0));

        Assert.Empty(await source.GetBooksAsync());
        Assert.Single(searches);
    }

    [Fact]
    public async Task GetBooksAsync_TorrentThatCantBeRead_IsLeftOut()
    {
        var (source, _, _) = Create(_ => Page(2, Record(11, "Dune"), Record(12, "Unknown Category", category: 99999)));

        var books = await source.GetBooksAsync();

        Assert.Equal("11", Assert.Single(books).ExternalId);
        Assert.Null(source.KnownTorrent("12"));
    }

    [Fact]
    public async Task GetBooksAsync_ReadAgain_ForgetsTorrentsNoLongerBookmarked()
    {
        var bookmarked = true;
        var (source, _, _) = Create(_ => bookmarked ? Page(1, Record(11, "Dune")) : Page(0));
        await source.GetBooksAsync();

        bookmarked = false;
        await source.GetBooksAsync();

        Assert.Null(source.KnownTorrent("11"));
    }
}
