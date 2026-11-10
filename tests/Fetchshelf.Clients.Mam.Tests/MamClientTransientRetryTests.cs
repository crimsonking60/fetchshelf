using System.Net;
using System.Text;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientTransientRetryTests
{
    private const string AccountJson = """
        {"ratio":2.0,"uploaded_bytes":10,"downloaded_bytes":5,"wedges":1,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
        """;

    private const string EmptySearchJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    private readonly List<TimeSpan> _delays = [];

    private MamClient ClientFor(Func<int, HttpRequestMessage, HttpResponseMessage> handler)
    {
        var attempt = 0;
        return new MamClient(
            new HttpClient(new FakeHttpMessageHandler(request => handler(++attempt, request))),
            new MamClientOptions { MamId = "cookie" },
            delay: (wait, _) =>
            {
                _delays.Add(wait);
                return Task.CompletedTask;
            });
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static TheoryData<string> NoAnswerFailures => ["connection", "timeout"];

    private static Exception NoAnswer(string kind) => kind == "timeout"
        ? new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.")
        : new HttpRequestException("Connection reset by peer");

    [Theory]
    [MemberData(nameof(NoAnswerFailures))]
    public async Task GetAccountStateAsync_NoAnswerOnce_IsSentAgainAfterTwoSeconds(string failure)
    {
        var client = ClientFor((attempt, _) => attempt == 1 ? throw NoAnswer(failure) : Ok(AccountJson));

        var state = await client.GetAccountStateAsync();

        Assert.Equal(20, state.UnsatLimit);
        Assert.Contains(TimeSpan.FromSeconds(2), _delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task GetAccountStateAsync_GatewayErrorOnce_IsSentAgain(HttpStatusCode status)
    {
        var client = ClientFor((attempt, _) => attempt == 1 ? new HttpResponseMessage(status) : Ok(AccountJson));

        var state = await client.GetAccountStateAsync();

        Assert.Equal(20, state.UnsatLimit);
    }

    [Fact]
    public async Task GetAccountStateAsync_NoAnswerTwice_Throws()
    {
        var attempts = 0;
        var client = ClientFor((attempt, _) =>
        {
            attempts = attempt;
            throw new HttpRequestException("Connection refused");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAccountStateAsync());

        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(MamRateLimitedException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(MamForbiddenException))]
    [InlineData((HttpStatusCode)406, typeof(MamAccountBlockedException))]
    [InlineData(HttpStatusCode.NotFound, typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(HttpRequestException))]
    public async Task GetAccountStateAsync_OtherRefusals_AreNotSentAgain(HttpStatusCode status, Type expected)
    {
        var attempts = 0;
        var client = ClientFor((attempt, _) =>
        {
            attempts = attempt;
            return new HttpResponseMessage(status);
        });

        await Assert.ThrowsAsync(expected, () => client.GetAccountStateAsync());

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task GetAccountStateAsync_CancelledByTheCaller_IsNotSentAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var client = ClientFor((attempt, _) =>
        {
            attempts = attempt;
            cancellation.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAccountStateAsync(cancellation.Token));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task SearchAsync_SentAgain_CarriesTheSameBody()
    {
        var bodies = new List<string>();
        var client = ClientFor((attempt, request) =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().Result);
            return attempt == 1 ? throw new HttpRequestException("Connection reset by peer") : Ok(EmptySearchJson);
        });

        await client.SearchAsync(new MamSearchQuery { Id = 42 });

        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.NotEmpty(bodies[0]);
    }

    [Fact]
    public async Task DownloadTorrentFileAsync_WithoutWedge_IsSentAgain()
    {
        var client = ClientFor((attempt, _) =>
            attempt == 1 ? throw new HttpRequestException("Connection reset by peer") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });

        var bytes = await client.DownloadTorrentFileAsync("hash", 1);

        Assert.Equal([1], bytes);
    }

    [Fact]
    public async Task DownloadTorrentFileAsync_WithWedge_IsNotSentAgain()
    {
        var attempts = 0;
        var client = ClientFor((attempt, _) =>
        {
            attempts = attempt;
            throw new HttpRequestException("Connection reset by peer");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadTorrentFileAsync("hash", 1, wedge: true));

        Assert.Equal(1, attempts);
    }
}
