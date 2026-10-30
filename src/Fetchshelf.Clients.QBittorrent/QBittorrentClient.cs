using System.Net;
using System.Net.Http.Json;

namespace Fetchshelf.Clients.QBittorrent;

/// <summary>
/// Typed client for the qBittorrent WebUI API v2, Fetchshelf-spec.md §5.2. One instance should be used per
/// configured qBittorrent connection (see <see cref="QBittorrentClientFactory"/>).
/// </summary>
public sealed class QBittorrentClient(HttpClient httpClient, QBittorrentClientOptions options)
{
    private readonly HashSet<string> _knownCategories = new(StringComparer.Ordinal);
    private string? _sessionId;
    private bool _authenticationNotRequired;

    public QBittorrentClientOptions Options => options;

    /// <summary>
    /// Authenticates with qBittorrent (§5.2). Success is detected by the presence of a session
    /// cookie in the response, since the login response body's content ("Ok."/"Fails.") is not
    /// consistent across qBittorrent versions. A successful login with no cookie is accepted only
    /// when qBittorrent then serves an authenticated endpoint without a session, which is what
    /// happens when authentication is bypassed for this client (localhost or a whitelisted subnet).
    /// </summary>
    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = options.Username,
            ["password"] = options.Password,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/v2/auth/login")) { Content = content };
        using var response = await httpClient.SendAsync(request, cancellationToken);

        var sessionId = ExtractSessionId(response);
        _sessionId = sessionId;
        _authenticationNotRequired = false;
        if (sessionId is not null)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode || body.Contains("Fails", StringComparison.OrdinalIgnoreCase)
            || !await ServesWithoutSessionAsync(cancellationToken))
        {
            throw new QBittorrentAuthenticationException("qBittorrent login failed: no session cookie was returned.");
        }

        _authenticationNotRequired = true;
    }

    private async Task<bool> ServesWithoutSessionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl("/api/v2/app/version"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<QBittorrentTorrent>> ListTorrentsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl("/api/v2/torrents/info")), cancellationToken);
        response.EnsureSuccessStatusCode();
        var torrents = await response.Content.ReadFromJsonAsync<List<QBittorrentTorrent>>(cancellationToken);
        return torrents ?? [];
    }

    public async Task<IReadOnlyList<QBittorrentFile>> ListFilesAsync(string hash, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl($"/api/v2/torrents/files?hash={Uri.EscapeDataString(hash)}")),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var files = await response.Content.ReadFromJsonAsync<List<QBittorrentFile>>(cancellationToken);
        return files ?? [];
    }

    public async Task AddTorrentAsync(
        byte[] torrentFileContent, string category, IReadOnlyList<string> tags, bool addStopped,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(category))
        {
            await EnsureCategoryExistsAsync(category, cancellationToken);
        }

        using var response = await SendAsync(() =>
        {
            var content = new MultipartFormDataContent
            {
                { new ByteArrayContent(torrentFileContent), "torrents", "upload.torrent" },
                { new StringContent(category), "category" },
                { new StringContent(string.Join(',', tags)), "tags" },
                // qBittorrent 5 renamed "paused" to "stopped" and ignores "paused"; older versions ignore "stopped".
                { new StringContent(addStopped ? "true" : "false"), "paused" },
                { new StringContent(addStopped ? "true" : "false"), "stopped" },
            };
            return new HttpRequestMessage(HttpMethod.Post, BuildUrl("/api/v2/torrents/add")) { Content = content };
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetCategoryAsync(string hash, string category, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(category))
        {
            await EnsureCategoryExistsAsync(category, cancellationToken);
        }

        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/setCategory", new Dictionary<string, string> { ["hashes"] = hash, ["category"] = category }),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddTagsAsync(string hash, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/addTags", new Dictionary<string, string> { ["hashes"] = hash, ["tags"] = string.Join(',', tags) }),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveTagsAsync(string hash, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/removeTags", new Dictionary<string, string> { ["hashes"] = hash, ["tags"] = string.Join(',', tags) }),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a qBittorrent category if it does not already exist, caching known categories per
    /// client instance so it is never created more than once (Fetchshelf-spec.md §5.2).
    /// </summary>
    public async Task EnsureCategoryExistsAsync(string category, CancellationToken cancellationToken = default)
    {
        if (_knownCategories.Contains(category))
        {
            return;
        }

        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/createCategory", new Dictionary<string, string> { ["category"] = category, ["savePath"] = string.Empty }),
            cancellationToken);

        // 409 means the category already exists (e.g. created by a prior process) - treat as success.
        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }

        _knownCategories.Add(category);
    }

    public async Task StartAsync(string hash, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/start", new Dictionary<string, string> { ["hashes"] = hash }), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(string hash, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/stop", new Dictionary<string, string> { ["hashes"] = hash }), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteWithDataAsync(string hash, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => PostForm("/api/v2/torrents/delete", new Dictionary<string, string> { ["hashes"] = hash, ["deleteFiles"] = "true" }),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> HashExistsAsync(string hash, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl($"/api/v2/torrents/info?hashes={Uri.EscapeDataString(hash)}")),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var torrents = await response.Content.ReadFromJsonAsync<List<QBittorrentTorrent>>(cancellationToken);
        return torrents is { Count: > 0 };
    }

    /// <summary>
    /// Sends a request, logging in first if no session exists yet, and re-authenticating once if the
    /// session has expired (Fetchshelf-spec.md §5.2).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        if (_sessionId is null && !_authenticationNotRequired)
        {
            await LoginAsync(cancellationToken);
        }

        var response = await SendWithSessionAsync(requestFactory(), cancellationToken);
        if (!IsUnauthenticated(response))
        {
            return response;
        }

        response.Dispose();
        await LoginAsync(cancellationToken);
        response = await SendWithSessionAsync(requestFactory(), cancellationToken);
        if (IsUnauthenticated(response))
        {
            response.Dispose();
            throw new QBittorrentAuthenticationException("qBittorrent session could not be re-established.");
        }

        return response;
    }

    private Task<HttpResponseMessage> SendWithSessionAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_sessionId is not null)
        {
            request.Headers.Add("Cookie", $"SID={_sessionId}");
        }

        return httpClient.SendAsync(request, cancellationToken);
    }

    private static bool IsUnauthenticated(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static string? ExtractSessionId(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var cookie in cookies)
        {
            var firstSegment = cookie.Split(';', 2)[0];
            var equalsIndex = firstSegment.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var name = firstSegment[..equalsIndex];
            var value = firstSegment[(equalsIndex + 1)..];
            if (name.Equals("SID", StringComparison.Ordinal) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private HttpRequestMessage PostForm(string path, Dictionary<string, string> form) =>
        new(HttpMethod.Post, BuildUrl(path)) { Content = new FormUrlEncodedContent(form) };

    private string BuildUrl(string path) => $"{options.BaseUrl.TrimEnd('/')}{path}";
}
