using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientAccountStateTests
{
    // Representative response shape.
    private const string ResponseJson = """
        {
            "classname": "Power User",
            "downloaded_bytes": 107374182400,
            "ratio": "5",
            "seedbonus": 1000,
            "snatch_summary": {
                "unsat": {"count": 3, "limit": 50, "name": "Unsatisfied", "size": 3000}
            },
            "uid": 1,
            "uploaded_bytes": 536870912000,
            "username": "example",
            "wedges": 2
        }
        """;

    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler, TimeProvider? timeProvider = null) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" }, timeProvider);

    [Fact]
    public async Task GetAccountStateAsync_ParsesAllDocumentedFields()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseJson) });

        var state = await client.GetAccountStateAsync();

        Assert.Equal(5, state.Ratio);
        Assert.Equal(536870912000, state.UploadedBytes);
        Assert.Equal(107374182400, state.DownloadedBytes);
        Assert.Equal(2, state.Wedges);
        Assert.Equal(1, state.Uid);
        Assert.Equal(3, state.UnsatCount);
        Assert.Equal(50, state.UnsatLimit);
    }

    [Fact]
    public async Task GetAccountStateAsync_RepeatedCallWithinWindow_ReturnsCachedValueWithoutNewRequest()
    {
        var requestCount = 0;
        var timeProvider = new ManualTimeProvider();
        var client = CreateClient(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseJson) };
        }, timeProvider);

        await client.GetAccountStateAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        await client.GetAccountStateAsync();

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task GetAccountStateAsync_CallAfterWindowExpires_IssuesNewRequest()
    {
        var requestCount = 0;
        var timeProvider = new ManualTimeProvider();
        var client = CreateClient(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseJson) };
        }, timeProvider);

        await client.GetAccountStateAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        await client.GetAccountStateAsync();

        Assert.Equal(2, requestCount);
    }
}
