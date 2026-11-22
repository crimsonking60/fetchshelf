using System.Net;

namespace Fetchshelf.Clients.Covers.Tests;

/// <summary>The Open Library and Audnexus clients against fake servers: what they send, what they read, and refusals.</summary>
public sealed class CoverClientTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    private static readonly CoverClientOptions Options = new()
    {
        UserAgent = "Fetchshelf/test",
        OpenLibrarySpacing = TimeSpan.Zero,
        AudnexusSpacing = TimeSpan.Zero,
        MaxImageBytes = 16,
    };

    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static OpenLibraryClient OpenLibrary(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), Options);

    private static AudnexusClient Audnexus(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), Options);

    [Fact]
    public async Task OpenLibrary_AsksForTheLargeCoverByIsbn_WithoutAPlaceholder_NamingOnlyFetchshelf()
    {
        HttpRequestMessage? sent = null;
        var cover = await OpenLibrary(request => { sent = request; return Bytes(Jpeg); }).GetCoverByIsbnAsync("9780765326355");

        Assert.Equal(Jpeg, cover);
        Assert.Equal("https://covers.openlibrary.org/b/isbn/9780765326355-L.jpg?default=false", sent!.RequestUri!.ToString());
        Assert.Equal("Fetchshelf/test", sent.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task OpenLibrary_NoCover_IsNull_AndATooLargeOneToo()
    {
        Assert.Null(await OpenLibrary(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).GetCoverByIsbnAsync("9780765326355"));
        Assert.Null(await OpenLibrary(_ => Bytes(new byte[17])).GetCoverByIsbnAsync("9780765326355"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OpenLibrary_RefusedOrFailing_Throws(HttpStatusCode status)
    {
        var exception = await Assert.ThrowsAsync<CoverServiceRefusedException>(
            () => OpenLibrary(_ => new HttpResponseMessage(status)).GetCoverByIsbnAsync("9780765326355"));

        Assert.Contains("Open Library answered " + (int)status, exception.Message);
    }

    [Fact]
    public async Task Audnexus_ReadsTheBook_ThenItsImageFromAmazonsImageHost()
    {
        var asked = new List<string>();
        var cover = await Audnexus(request =>
        {
            asked.Add(request.RequestUri!.ToString());
            return request.RequestUri.Host == "api.audnex.us"
                ? Json("""{"asin":"B00ABCDEFG","title":"A Book","image":"https://m.media-amazon.com/images/I/cover.jpg"}""")
                : Bytes(Jpeg);
        }).GetCoverByAsinAsync("B00ABCDEFG");

        Assert.Equal(Jpeg, cover);
        Assert.Equal(["https://api.audnex.us/books/B00ABCDEFG", "https://m.media-amazon.com/images/I/cover.jpg"], asked);
    }

    [Theory]
    [InlineData("""{"image":"https://example.com/cover.jpg"}""")]
    [InlineData("""{"image":"http://m.media-amazon.com/images/I/cover.jpg"}""")]
    [InlineData("""{"image":"https://media-amazon.com.example.com/cover.jpg"}""")]
    [InlineData("""{"title":"No image"}""")]
    public async Task Audnexus_AnImageElsewhereOrNone_IsNotFollowed(string book)
    {
        var asked = 0;
        var cover = await Audnexus(_ => { asked++; return Json(book); }).GetCoverByAsinAsync("B00ABCDEFG");

        Assert.Null(cover);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Audnexus_UnknownBook_IsNull_AndARefusalThrows()
    {
        Assert.Null(await Audnexus(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).GetCoverByAsinAsync("B00ABCDEFG"));
        await Assert.ThrowsAsync<CoverServiceRefusedException>(
            () => Audnexus(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)).GetCoverByAsinAsync("B00ABCDEFG"));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task RequestSpacer_WaitsOnlyTheRestOfTheSpacing()
    {
        var time = new ManualTime();
        var waits = new List<TimeSpan>();
        var spacer = new RequestSpacer(TimeSpan.FromSeconds(3), time, (wait, _) => { waits.Add(wait); time.Now += wait; return Task.CompletedTask; });

        await spacer.WaitTurnAsync(CancellationToken.None);
        time.Now += TimeSpan.FromSeconds(1);
        await spacer.WaitTurnAsync(CancellationToken.None);
        time.Now += TimeSpan.FromSeconds(5);
        await spacer.WaitTurnAsync(CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }
}
