using System.Net;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientForbiddenTests
{
    private const string EmptyResultJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    private static MamClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> handler, ManualTimeProvider? time = null, ILogger<MamClient>? logger = null) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" }, time, logger: logger);

    [Fact]
    public async Task Search_403Response_ThrowsForbiddenWithAnActionableMessage()
    {
        var client = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var exception = await Assert.ThrowsAsync<MamForbiddenException>(() => client.SearchAsync(new MamSearchQuery()));

        Assert.Contains("HTTP 403", exception.Message);
        Assert.Contains("mam_id", exception.Message);
        Assert.Contains("restart", exception.Message);
    }

    [Fact]
    public async Task Search_AfterA403_DoesNotCallMamAgainUntilThePauseHasPassed()
    {
        var time = new ManualTimeProvider();
        var requests = 0;
        var forbidden = true;
        var client = ClientFor(_ =>
        {
            requests++;
            return forbidden
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
        }, time);
        await Assert.ThrowsAsync<MamForbiddenException>(() => client.SearchAsync(new MamSearchQuery()));

        time.Advance(TimeSpan.FromMinutes(29));
        var paused = await Assert.ThrowsAsync<MamForbiddenException>(() => client.SearchAsync(new MamSearchQuery()));
        Assert.Equal(1, requests); // the second call never reached the network
        Assert.Contains("paused until", paused.Message);

        forbidden = false;
        time.Advance(TimeSpan.FromMinutes(2));
        await client.SearchAsync(new MamSearchQuery());
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task DownloadTorrentFile_403_ThrowsForbiddenButDoesNotPauseOtherCalls()
    {
        var client = ClientFor(request => request.RequestUri!.AbsolutePath.Contains("download.php")
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) });

        var exception = await Assert.ThrowsAsync<MamForbiddenException>(() => client.DownloadTorrentFileAsync("hash", 1));
        await client.SearchAsync(new MamSearchQuery());

        Assert.DoesNotContain("paused", exception.Message);
    }

    [Fact]
    public async Task FailedCall_IsLoggedWithStatusContentTypeAndStartOfTheBody()
    {
        var logger = new ListLogger();
        var client = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<html>\n  <title>Attention Required!   Cloudflare</title>\n</html>", System.Text.Encoding.UTF8, "text/html"),
        }, logger: logger);

        await Assert.ThrowsAsync<MamForbiddenException>(() => client.SearchAsync(new MamSearchQuery()));

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("HTTP 403", entry.Message);
        Assert.Contains("text/html", entry.Message);
        Assert.Contains("<html> <title>Attention Required! Cloudflare</title> </html>", entry.Message); // whitespace collapsed
    }

    [Fact]
    public async Task FailedCall_LongBodyIsTruncated_AndDownloadHashIsNeverLogged()
    {
        var logger = new ListLogger();
        var client = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(new string('x', 2000)) }, logger: logger);

        await Assert.ThrowsAsync<MamForbiddenException>(() => client.DownloadTorrentFileAsync("secrettoken123", 1));

        var message = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.DoesNotContain("secrettoken123", message);
        Assert.Contains("/tor/download.php/<hash>", message);
        Assert.True(message.Length < 600, $"log line was {message.Length} characters");
        Assert.DoesNotContain("cookie", message); // the mam_id is never logged
    }

    [Fact]
    public async Task ServerError_IsLoggedWithoutConsumingTheBodyTheCallerReads()
    {
        var logger = new ListLogger();
        var client = ClientFor(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("upstream down") }, logger: logger);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(new MamSearchQuery()));

        var message = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning).Message;
        Assert.Contains("HTTP 502", message);
        Assert.DoesNotContain("upstream down", message);
    }

    private sealed class ListLogger : ILogger<MamClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
