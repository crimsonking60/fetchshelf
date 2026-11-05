using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Web.Tests;

public class QBittorrentPresenceCacheTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class HttpClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (QBittorrentPresenceCache Cache, Clock Clock, Func<int> Reads, Action<string> SetCategory) Create()
    {
        var reads = 0;
        var category = "first";
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("auth/login"))
            {
                var login = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                login.Headers.Add("Set-Cookie", "SID=abc; path=/");
                return login;
            }

            Interlocked.Increment(ref reads);
            var torrents = new[] { new QBittorrentTorrent { Hash = "h1", Name = "T", SavePath = "/d", State = "uploading", Comment = "MID=1", Category = category } };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
        });
        var settings = new FetchshelfSettings { QBittorrent = [new QBittorrentConnection { Url = "http://qbittorrent.test", Username = "u", Password = "p" }] };
        var clock = new Clock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        var cache = new QBittorrentPresenceCache(
            settings, new QBittorrentClientFactory(new HttpClients(handler)), NullLogger<QBittorrentPresenceCache>.Instance, clock);
        return (cache, clock, () => Volatile.Read(ref reads), c => category = c);
    }

    [Fact]
    public async Task GetAsync_WithinMaxAge_ReadsQBittorrentOnce()
    {
        var (cache, clock, reads, _) = Create();

        var first = await cache.GetAsync();
        clock.Now += TimeSpan.FromMinutes(1);
        await cache.GetAsync();

        Assert.True(first.Loaded);
        Assert.Equal("first", first.CategoryByMamId[1]);
        Assert.Equal(1, reads());
    }

    [Fact]
    public async Task GetAsync_AfterMaxAge_AnswersAtOnce_AndReadsAgainInTheBackground()
    {
        var (cache, clock, reads, setCategory) = Create();
        await cache.GetAsync();
        setCategory("second");
        clock.Now += QBittorrentPresenceCache.MaxAge + TimeSpan.FromSeconds(1);

        var stale = await cache.GetAsync();
        QBittorrentPresence fresh;
        var waited = 0;
        do
        {
            await Task.Delay(20);
            fresh = await cache.GetAsync();
        }
        while (fresh.CategoryByMamId[1] != "second" && ++waited < 250);

        Assert.Equal("first", stale.CategoryByMamId[1]);
        Assert.Equal("second", fresh.CategoryByMamId[1]);
        Assert.Equal(2, reads());
    }

    [Fact]
    public void CategoryOf_FallsBackToAKnownHash()
    {
        var presence = QBittorrentPresence.From([
            new QBittorrentTorrent { Hash = "ABC", Name = "T", SavePath = "/d", State = "uploading", Category = "books" },
        ]);

        Assert.Equal("books", presence.CategoryOf(5, ["abc"]));
        Assert.Null(presence.CategoryOf(5, ["def"]));
    }
}
