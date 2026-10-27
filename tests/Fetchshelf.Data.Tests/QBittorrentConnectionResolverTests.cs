using System.Net;
using System.Net.Http.Json;
using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public class QBittorrentConnectionResolverTests
{
    private static QBittorrentClient CreateClient(bool knowsHash)
    {
        var options = new QBittorrentClientOptions("http://localhost:8080", "admin", "adminadmin");
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("auth/login"))
            {
                var loginResponse = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                loginResponse.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
                return loginResponse;
            }

            var torrents = knowsHash ? new List<QBittorrentTorrent>
            {
                new() { Hash = "abc", Name = "Book", SavePath = "/downloads", State = "uploading" },
            } : [];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(torrents) };
        });
        return new QBittorrentClient(new HttpClient(handler), options);
    }

    [Fact]
    public async Task ResolveAsync_FirstConnectionReportsHashPresent_ReturnsThatConnection()
    {
        var first = CreateClient(knowsHash: true);
        var second = CreateClient(knowsHash: true);

        var result = await QBittorrentConnectionResolver.ResolveAsync([first, second], "abc");

        Assert.Same(first, result);
    }

    [Fact]
    public async Task ResolveAsync_NoConnectionKnowsHash_ReturnsNull()
    {
        var only = CreateClient(knowsHash: false);

        var result = await QBittorrentConnectionResolver.ResolveAsync([only], "abc");

        Assert.Null(result);
    }
}
