using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientDownloadTests
{
    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler, TimeProvider? timeProvider = null, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" }, timeProvider, delay);

    [Fact]
    public async Task DownloadTorrentFileAsync_PlainDownload_ReturnsExpectedContent()
    {
        var expectedBytes = "torrent-file-content"u8.ToArray();
        string? requestedUrl = null;
        var client = CreateClient(request =>
        {
            requestedUrl = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expectedBytes) };
        });

        var result = await client.DownloadTorrentFileAsync("abc123", 1234567890);

        Assert.Equal(expectedBytes, result);
        Assert.Equal("https://www.myanonamouse.net/tor/download.php/abc123?tid=1234567890", requestedUrl);
    }

    [Fact]
    public async Task DownloadTorrentFileAsync_Wedged_IncludesFlInRequestUrl()
    {
        string? requestedUrl = null;
        var client = CreateClient(request =>
        {
            requestedUrl = request.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        });

        await client.DownloadTorrentFileAsync("abc123", 1234567890, wedge: true);

        Assert.Equal("https://www.myanonamouse.net/tor/download.php/abc123?tid=1234567890&fl", requestedUrl);
    }

    [Fact]
    public async Task DownloadTorrentFileAsync_ConsecutiveDownloads_AreTwoSecondsApart()
    {
        var timeProvider = new ManualTimeProvider();
        var requestedDelays = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> fakeDelay = (wait, _) =>
        {
            requestedDelays.Add(wait);
            timeProvider.Advance(wait);
            return Task.CompletedTask;
        };
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }, timeProvider, fakeDelay);

        await client.DownloadTorrentFileAsync("abc123", 1);
        timeProvider.Advance(TimeSpan.FromMilliseconds(200)); // only 200ms passed before the next download
        await client.DownloadTorrentFileAsync("def456", 2);

        Assert.Equal(TimeSpan.FromMilliseconds(1800), Assert.Single(requestedDelays));
    }
}
