using System.Net;
using System.Text;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientCallReportTests
{
    private const string AccountJson = """
        {"ratio":2.0,"uploaded_bytes":10,"downloaded_bytes":5,"wedges":1,"uid":1,"snatch_summary":{"unsat":{"count":0,"limit":20}}}
        """;

    private static (MamClient Client, List<MamCall> Calls) ClientFor(Func<int, HttpResponseMessage> answer)
    {
        var attempt = 0;
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ => answer(++attempt))),
            new MamClientOptions { MamId = "cookie" },
            delay: (_, _) => Task.CompletedTask);
        var calls = new List<MamCall>();
        client.CallCompleted += calls.Add;
        return (client, calls);
    }

    [Fact]
    public async Task AnsweredCall_IsReportedWithItsStatusAndPath()
    {
        var (client, calls) = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AccountJson, Encoding.UTF8, "application/json") });

        await client.GetAccountStateAsync();

        var call = Assert.Single(calls);
        Assert.Equal(("GET", "www.myanonamouse.net", "/jsonLoad.php", 200, (string?)null), (call.Method, call.Host, call.Path, call.StatusCode, call.Failure));
        Assert.True(call.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task RetriedCall_IsReportedOncePerAttempt()
    {
        var (client, calls) = ClientFor(attempt => attempt == 1
            ? throw new HttpRequestException("Connection reset by peer")
            : new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<MamRateLimitedException>(() => client.GetAccountStateAsync());

        Assert.Equal([(null, "Connection failed"), (429, null)], calls.Select(c => (c.StatusCode, c.Failure)));
    }

    [Fact]
    public async Task Download_ReportsThePathWithoutItsToken()
    {
        var (client, calls) = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        await client.DownloadTorrentFileAsync("secrettoken", 1);

        Assert.Equal("/tor/download.php/<hash>", Assert.Single(calls).Path);
    }
}
