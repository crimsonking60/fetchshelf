using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Fetchshelf.Clients.QBittorrent.Tests;

public class QBittorrentClientTests
{
    private static readonly QBittorrentClientOptions Options = new("http://localhost:8080", "admin", "adminadmin");

    private static HttpResponseMessage LoginOkResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
        response.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
        return response;
    }

    private static QBittorrentClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), Options);

    [Fact]
    public async Task LoginAsync_CapturesSessionCookie_AttachedOnSubsequentRequest()
    {
        string? cookieOnSecondRequest = null;
        var requestCount = 0;
        var client = CreateClient(request =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                return LoginOkResponse();
            }

            cookieOnSecondRequest = request.Headers.TryGetValues("Cookie", out var values) ? values.First() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<QBittorrentTorrent>()),
            };
        });

        await client.LoginAsync();
        await client.ListTorrentsAsync();

        Assert.Equal("SID=abc123", cookieOnSecondRequest);
    }

    [Fact]
    public async Task ListTorrentsAsync_NoExistingSession_LogsInFirst()
    {
        var requestCount = 0;
        var client = CreateClient(request =>
        {
            requestCount++;
            return requestCount == 1
                ? LoginOkResponse()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<QBittorrentTorrent>()) };
        });

        await client.ListTorrentsAsync();

        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task SendAsync_UnauthenticatedResponse_ReAuthenticatesAndRetriesOnce()
    {
        var requestCount = 0;
        var client = CreateClient(request =>
        {
            requestCount++;
            return requestCount switch
            {
                1 => LoginOkResponse(), // initial login
                2 => new HttpResponseMessage(HttpStatusCode.Forbidden), // session expired
                3 => LoginOkResponse(), // re-login
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<QBittorrentTorrent>()) },
            };
        });

        var torrents = await client.ListTorrentsAsync();

        Assert.Equal(4, requestCount);
        Assert.Empty(torrents);
    }

    [Fact]
    public async Task SendAsync_StillUnauthenticatedAfterRetry_ThrowsAuthenticationException()
    {
        var client = CreateClient(request => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? LoginOkResponse()
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<QBittorrentAuthenticationException>(() => client.ListTorrentsAsync());
    }

    [Fact]
    public async Task LoginAsync_NoSessionCookieReturned_ThrowsAuthenticationException()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Fails.") });

        await Assert.ThrowsAsync<QBittorrentAuthenticationException>(() => client.LoginAsync());
    }

    [Fact]
    public async Task LoginAsync_NoCookieButServesWithoutSession_TreatedAsAuthenticationBypassed()
    {
        var paths = new List<string>();
        string? cookieOnList = "unset";
        var client = CreateClient(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            switch (request.RequestUri.AbsolutePath)
            {
                case "/api/v2/auth/login":
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/api/v2/app/version":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("v5.0.0") };
                default:
                    cookieOnList = request.Headers.TryGetValues("Cookie", out var values) ? values.First() : null;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<QBittorrentTorrent>()) };
            }
        });

        await client.ListTorrentsAsync();
        await client.ListTorrentsAsync();

        Assert.Null(cookieOnList);
        Assert.Equal(1, paths.Count(p => p == "/api/v2/auth/login"));
    }

    [Fact]
    public async Task LoginAsync_NoCookieAndVersionEndpointRequiresSession_ThrowsAuthenticationException()
    {
        var client = CreateClient(request => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<QBittorrentAuthenticationException>(() => client.LoginAsync());
    }

    [Fact]
    public async Task ListTorrentsAsync_ParsesAllSixFields()
    {
        var client = CreateClient(request => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? LoginOkResponse()
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<QBittorrentTorrent>
                {
                    new()
                    {
                        Hash = "abc123",
                        Name = "Some Book",
                        SavePath = "/downloads/some-book",
                        Category = "audiobooks",
                        Tags = "fetchshelf,audiobook",
                        Progress = 1.0,
                        State = "stoppedUP",
                    },
                }),
            });

        var torrents = await client.ListTorrentsAsync();

        var torrent = Assert.Single(torrents);
        Assert.Equal("abc123", torrent.Hash);
        Assert.Equal("Some Book", torrent.Name);
        Assert.Equal("/downloads/some-book", torrent.SavePath);
        Assert.Equal("audiobooks", torrent.Category);
        Assert.Equal(["fetchshelf", "audiobook"], torrent.TagList);
        Assert.Equal(1.0, torrent.Progress);
        Assert.Equal("stoppedUP", torrent.State);
    }

    [Fact]
    public async Task ListFilesAsync_ParsesFileList()
    {
        var client = CreateClient(request => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? LoginOkResponse()
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<QBittorrentFile>
                {
                    new() { Name = "book.m4b", Size = 123456 },
                }),
            });

        var files = await client.ListFilesAsync("abc123");

        var file = Assert.Single(files);
        Assert.Equal("book.m4b", file.Name);
        Assert.Equal(123456, file.Size);
    }

    [Fact]
    public async Task AddTorrentAsync_WithCategoryAndTags_PostsExpectedFields()
    {
        string? capturedCategory = null;
        string? capturedTags = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/createCategory")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/add")
            {
                var multipart = (MultipartFormDataContent)request.Content!;
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name == "category")
                    {
                        capturedCategory = part.ReadAsStringAsync().Result;
                    }
                    else if (name == "tags")
                    {
                        capturedTags = part.ReadAsStringAsync().Result;
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        await client.AddTorrentAsync(Encoding.UTF8.GetBytes("fake torrent bytes"), "audiobooks", ["tag1", "tag2"], addStopped: false);

        Assert.Equal("audiobooks", capturedCategory);
        Assert.Equal("tag1,tag2", capturedTags);
    }

    [Fact]
    public async Task AddTorrentAsync_StoppedFlagSet_PostsPausedTrue()
    {
        string? capturedPaused = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/add")
            {
                var multipart = (MultipartFormDataContent)request.Content!;
                foreach (var part in multipart)
                {
                    if (part.Headers.ContentDisposition?.Name?.Trim('"') == "paused")
                    {
                        capturedPaused = part.ReadAsStringAsync().Result;
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        await client.AddTorrentAsync(Encoding.UTF8.GetBytes("fake"), category: "", tags: [], addStopped: true);

        Assert.Equal("true", capturedPaused);
    }

    [Fact]
    public async Task SetCategoryAsync_ChangesCategory()
    {
        string? capturedBody = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/createCategory")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (request.RequestUri.AbsolutePath == "/api/v2/torrents/setCategory")
            {
                capturedBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        await client.SetCategoryAsync("abc123", "audiobooks");

        Assert.Contains("hashes=abc123", capturedBody);
        Assert.Contains("category=audiobooks", capturedBody);
    }

    [Fact]
    public async Task AddTagsAsync_AddsTags()
    {
        string? capturedBody = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            capturedBody = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.AddTagsAsync("abc123", ["new-tag"]);

        Assert.Contains("tags=new-tag", capturedBody);
    }

    [Fact]
    public async Task RemoveTagsAsync_RemovesTags()
    {
        string? capturedBody = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            capturedBody = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.RemoveTagsAsync("abc123", ["old-tag"]);

        Assert.Contains("tags=old-tag", capturedBody);
    }

    [Fact]
    public async Task EnsureCategoryExistsAsync_FirstUse_CreatesCategory()
    {
        var createCategoryCallCount = 0;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            createCategoryCallCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.EnsureCategoryExistsAsync("audiobooks");

        Assert.Equal(1, createCategoryCallCount);
    }

    [Fact]
    public async Task EnsureCategoryExistsAsync_SecondUseSameCategory_DoesNotRecreate()
    {
        var createCategoryCallCount = 0;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            createCategoryCallCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.EnsureCategoryExistsAsync("audiobooks");
        await client.EnsureCategoryExistsAsync("audiobooks");

        Assert.Equal(1, createCategoryCallCount);
    }

    [Fact]
    public async Task StartAsync_StartsTorrent()
    {
        string? capturedPath = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            capturedPath = request.RequestUri.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.StartAsync("abc123");

        Assert.Equal("/api/v2/torrents/start", capturedPath);
    }

    [Fact]
    public async Task StopAsync_StopsTorrent()
    {
        string? capturedPath = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            capturedPath = request.RequestUri.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.StopAsync("abc123");

        Assert.Equal("/api/v2/torrents/stop", capturedPath);
    }

    [Fact]
    public async Task DeleteWithDataAsync_DeletesTorrentAndData()
    {
        string? capturedBody = null;
        var client = CreateClient(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/login")
            {
                return LoginOkResponse();
            }

            capturedBody = request.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await client.DeleteWithDataAsync("abc123");

        Assert.Contains("hashes=abc123", capturedBody);
        Assert.Contains("deleteFiles=true", capturedBody);
    }

    [Fact]
    public async Task HashExistsAsync_HashPresent_ReturnsTrue()
    {
        var client = CreateClient(request => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? LoginOkResponse()
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<QBittorrentTorrent>
                {
                    new() { Hash = "abc123", Name = "x", SavePath = "/x", State = "stoppedUP" },
                }),
            });

        Assert.True(await client.HashExistsAsync("abc123"));
    }

    [Fact]
    public async Task HashExistsAsync_HashNotPresent_ReturnsFalse()
    {
        var client = CreateClient(request => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? LoginOkResponse()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<QBittorrentTorrent>()) });

        Assert.False(await client.HashExistsAsync("doesnotexist"));
    }
}
