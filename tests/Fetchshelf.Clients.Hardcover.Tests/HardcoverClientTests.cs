using System.Net;
using System.Text;
using System.Text.Json;

namespace Fetchshelf.Clients.Hardcover.Tests;

public sealed class HardcoverClientTests
{
    private const string Endpoint = "https://hardcover.test/v1/graphql";

    private static HardcoverClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler, string token = "secret-token", List<TimeSpan>? delays = null) =>
        new(
            new HttpClient(new FakeHttpMessageHandler(handler)),
            new HardcoverClientOptions { Token = token, UserAgent = "Fetchshelf/test", Endpoint = Endpoint },
            (wait, _) =>
            {
                delays?.Add(wait);
                return Task.CompletedTask;
            });

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>The query text and variables a request sent.</summary>
    private static (string Query, JsonElement Variables) ReadBody(HttpRequestMessage request)
    {
        using var document = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        return (document.RootElement.GetProperty("query").GetString()!, document.RootElement.GetProperty("variables").Clone());
    }

    private const string DuneBook = """
        {
          "id": 312460,
          "title": "Dune",
          "slug": "dune",
          "contributions": [
            { "contribution": null, "author": { "name": "Frank Herbert" } },
            { "contribution": "Narrator", "author": { "name": "Scott Brick" } },
            { "contribution": "Author", "author": { "name": "Frank Herbert" } }
          ],
          "book_series": [ { "position": 1, "series": { "name": "Dune" } }, { "position": null, "series": { "name": "Dune Universe" } } ],
          "image": { "url": "https://assets.hardcover.app/dune.jpg" }
        }
        """;

    [Fact]
    public async Task GetMeAsync_ReadsTheUser_AndSendsTheTokenAndUserAgent()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;
            return Json("""{ "data": { "me": [ { "id": 42, "username": "reader" } ] } }""");
        }, token: "Bearer secret-token");

        var me = await client.GetMeAsync();

        Assert.Equal(new HardcoverUser(42, "reader"), me);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal(Endpoint, sent.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", sent.Headers.Authorization.Parameter);
        Assert.Contains("Fetchshelf/test", sent.Headers.UserAgent.ToString());
        Assert.Contains("me { id username }", ReadBody(sent).Query);
    }

    [Fact]
    public async Task GetBooksWithStatusAsync_SendsTheStatusAsAVariable_AndReadsTheBooks()
    {
        (string Query, JsonElement Variables)? body = null;
        var client = CreateClient(request =>
        {
            body = ReadBody(request);
            return Json($$"""{ "data": { "user_books": [ { "book": {{DuneBook}} } ] } }""");
        });

        var books = await client.GetBooksWithStatusAsync(42, HardcoverReadingStatus.WantToRead);

        var variables = body!.Value.Variables;
        Assert.Equal(42, variables.GetProperty("userId").GetInt64());
        Assert.Equal(1, variables.GetProperty("statusId").GetInt32());
        Assert.Equal(0, variables.GetProperty("offset").GetInt32());
        Assert.DoesNotContain("42", body.Value.Query, StringComparison.Ordinal);

        var dune = Assert.Single(books);
        Assert.Equal(312460, dune.Id);
        Assert.Equal("Dune", dune.Title);
        Assert.Equal(["Frank Herbert"], dune.Authors);
        Assert.Equal([new HardcoverSeriesEntry("Dune", 1), new HardcoverSeriesEntry("Dune Universe", null)], dune.Series);
        Assert.Equal("https://assets.hardcover.app/dune.jpg", dune.CoverUrl);
        Assert.Equal("https://hardcover.app/books/dune", dune.Url);
    }

    [Fact]
    public async Task GetBooksWithStatusAsync_ReadsAFullPageAndAsksForTheNext()
    {
        var offsets = new List<int>();
        var client = CreateClient(request =>
        {
            var offset = ReadBody(request).Variables.GetProperty("offset").GetInt32();
            offsets.Add(offset);
            var count = offset == 0 ? HardcoverClient.PageSize : 3;
            var rows = string.Join(",", Enumerable.Range(offset, count).Select(i => $$"""{ "book": { "id": {{i}}, "title": "Book {{i}}" } }"""));
            return Json($$"""{ "data": { "user_books": [ {{rows}} ] } }""");
        });

        var books = await client.GetBooksWithStatusAsync(42, HardcoverReadingStatus.WantToRead);

        Assert.Equal([0, HardcoverClient.PageSize], offsets);
        Assert.Equal(HardcoverClient.PageSize + 3, books.Count);
        Assert.Empty(books[0].Authors);
        Assert.Null(books[0].Url);
    }

    [Fact]
    public async Task GetListBooksAsync_ReadsTheListsBooks()
    {
        JsonElement? variables = null;
        var client = CreateClient(request =>
        {
            variables = ReadBody(request).Variables;
            return Json($$"""{ "data": { "lists": [ { "id": 7, "name": "To fetch", "list_books": [ { "book": {{DuneBook}} } ] } ] } }""");
        });

        var books = await client.GetListBooksAsync(42, "to-fetch");

        Assert.Equal("to-fetch", variables!.Value.GetProperty("slug").GetString());
        Assert.Equal("Dune", Assert.Single(books).Title);
    }

    [Fact]
    public async Task GetListBooksAsync_UnknownSlug_SaysSo()
    {
        var client = CreateClient(_ => Json("""{ "data": { "lists": [] } }"""));

        var ex = await Assert.ThrowsAsync<HardcoverException>(() => client.GetListBooksAsync(42, "missing"));

        Assert.Contains("'missing'", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_SaysTheTokenIsWrongOrExpired()
    {
        var client = CreateClient(_ => Json("""{ "error": "invalid_token" }""", HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<HardcoverAuthenticationException>(() => client.GetMeAsync());

        Assert.Contains("wrong or has expired", ex.Message);
    }

    [Fact]
    public async Task Forbidden_NamesTheMissingPermission()
    {
        var client = CreateClient(_ => Json(
            """{ "error": "insufficient_scope", "error_description": "missing scope", "scope": "read:lists" }""", HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<HardcoverAuthenticationException>(() => client.GetListBooksAsync(42, "to-fetch"));

        Assert.Contains("read:lists", ex.Message);
    }

    [Fact]
    public async Task RateLimited_WaitsForRetryAfterOnce_ThenRetries()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;
        var client = CreateClient(_ =>
        {
            if (calls++ == 0)
            {
                var limited = Json("""{ "error": "Too Many Requests" }""", HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
                return limited;
            }

            return Json("""{ "data": { "me": [ { "id": 1, "username": "reader" } ] } }""");
        }, delays: delays);

        var me = await client.GetMeAsync();

        Assert.Equal(1, me.Id);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public async Task RateLimitedTwice_GivesUp()
    {
        var client = CreateClient(_ => Json("""{ "error": "Too Many Requests" }""", HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<HardcoverException>(() => client.GetMeAsync());

        Assert.Contains("rate limit", ex.Message);
    }

    [Fact]
    public async Task GraphQlErrors_AreReported()
    {
        var client = CreateClient(_ => Json("""{ "errors": [ { "message": "field 'book_series' not found" } ] }"""));

        var ex = await Assert.ThrowsAsync<HardcoverException>(() => client.GetBooksWithStatusAsync(42, HardcoverReadingStatus.WantToRead));

        Assert.Contains("field 'book_series' not found", ex.Message);
    }

    [Fact]
    public async Task ServerError_IsReportedWithItsStatus()
    {
        var client = CreateClient(_ => Json("""{ "error": "Service temporarily unavailable" }""", HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<HardcoverException>(() => client.GetMeAsync());

        Assert.Contains("503", ex.Message);
        Assert.Contains("Service temporarily unavailable", ex.Message);
    }
}
