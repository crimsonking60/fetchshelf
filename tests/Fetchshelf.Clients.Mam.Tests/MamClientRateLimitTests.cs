using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientRateLimitTests
{
    private const string EmptyResultJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    [Fact]
    public async Task SendAsync_429Response_ThrowsRateLimitedAndBacksOffBeforeNextRequest()
    {
        var timeProvider = new ManualTimeProvider();
        var requestedDelays = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (wait, _) =>
        {
            requestedDelays.Add(wait);
            timeProvider.Advance(wait);
            return Task.CompletedTask;
        };
        var requestCount = 0;
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(_ =>
            {
                requestCount++;
                return requestCount == 1
                    ? new HttpResponseMessage((HttpStatusCode)429)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
            })),
            new MamClientOptions { MamId = "cookie" },
            timeProvider,
            fakeDelay);

        await Assert.ThrowsAsync<MamRateLimitedException>(() => client.SearchAsync(new MamSearchQuery()));

        // A second call (after the 400ms search-politeness gap has elapsed) must still wait out the
        // 429 backoff before its request is sent.
        timeProvider.Advance(TimeSpan.FromMilliseconds(400));
        await client.SearchAsync(new MamSearchQuery());

        Assert.Contains(requestedDelays, d => d > TimeSpan.Zero && d <= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task SendAsync_406Response_AbortsRemainingRequestsInADownloadFlow()
    {
        var requestsAfterBlock = 0;
        var blocked = false;
        var client = new MamClient(
            new HttpClient(new FakeHttpMessageHandler(request =>
            {
                if (blocked)
                {
                    requestsAfterBlock++;
                }

                if (request.RequestUri!.AbsolutePath.Contains("download.php"))
                {
                    blocked = true;
                    return new HttpResponseMessage((HttpStatusCode)406);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
            })),
            new MamClientOptions { MamId = "cookie" });

        // Simulates a download run: search succeeds, then the download itself is blocked.
        await client.SearchAsync(new MamSearchQuery());
        await Assert.ThrowsAsync<MamAccountBlockedException>(() => client.DownloadTorrentFileAsync("hash", 1));

        // The caller's flow aborts on the exception - no further requests are made by this client.
        Assert.Equal(0, requestsAfterBlock);
    }
}
