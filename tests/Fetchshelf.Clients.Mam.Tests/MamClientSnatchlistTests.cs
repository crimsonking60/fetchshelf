using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientSnatchlistTests
{
    private const string AccountStateJson = """
        {
            "ratio": "5",
            "uploaded_bytes": 1,
            "downloaded_bytes": 1,
            "wedges": 0,
            "uid": 42,
            "snatch_summary": {"unsat": {"count": 0, "limit": 50}}
        }
        """;

    private static string SnatchlistRowsJson(int count) =>
        $$"""{"success":true,"rows":[{{string.Join(',', Enumerable.Range(0, count).Select(i => $$"""{"id":{{i}},"title":"Book {{i}}","seeders":1,"leechers":0,"finished":true,"dl":"hash{{i}}"}"""))}}]}""";

    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" });

    [Fact]
    public async Task GetSnatchListAsync_RequestsCorrectKindAndOnlyReturnsThoseRows()
    {
        string? requestedUrl = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            requestedUrl = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnatchlistRowsJson(2)) };
        });

        var rows = await client.GetSnatchListAsync(MamSnatchlistKind.SeedingUnsat, page: 0, DateTimeOffset.UtcNow);

        Assert.Contains("type=seedUnsat", requestedUrl);
        Assert.Contains("cdn.myanonamouse.net", requestedUrl);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task GetSnatchListAsync_PageOfMaximumSize_ReturnsAllRowsUnmodified()
    {
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountStateJson) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnatchlistRowsJson(250)) };
        });

        var rows = await client.GetSnatchListAsync(MamSnatchlistKind.Unsat, page: 0, DateTimeOffset.UtcNow);

        Assert.Equal(250, rows.Count);
    }
}
