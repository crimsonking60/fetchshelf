using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Typed client for the MAM HTTP API.
/// </summary>
public sealed partial class MamClient(
    HttpClient httpClient,
    MamClientOptions options,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILogger<MamClient>? logger = null)
{
    private const string CookieName = "mam_id";
    private static readonly TimeSpan AccountStateCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to back off after a 429 before the next request.
    /// 60s is a conservative default for an unofficial API.
    /// </summary>
    private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to stop calling MAM after an HTTP 403. Retrying a rejected `mam_id` every job interval gains nothing and
    /// risks a longer block; fixing `mam_id` means editing config.toml and restarting, which also clears this pause.
    /// </summary>
    private static readonly TimeSpan ForbiddenPause = TimeSpan.FromMinutes(30);

    private const int LoggedBodyLength = 300;

    /// <summary>How long to wait before sending a request again that got no answer or a 502/503/504.</summary>
    private static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether <paramref name="ex"/>, thrown by sending a request, means MAM never answered: the connection failed, or
    /// the client's timeout ran out. A cancellation the caller asked for is not one.
    /// </summary>
    public static bool IsNoAnswer(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException { StatusCode: null }
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>A short, readable cause for a request that got no answer.</summary>
    public static string DescribeNoAnswer(Exception ex) => ex is TaskCanceledException
        ? "MAM did not answer in time"
        : $"the connection to MAM failed: {ex.Message}";

    private readonly ILogger<MamClient> _logger = logger ?? NullLogger<MamClient>.Instance;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Overridable so tests can prove politeness delays are requested without sleeping for the real interval.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, timeProvider ?? TimeProvider.System, ct));

    private readonly SemaphoreSlim _accountStateLock = new(1, 1);

    /// <summary>
    /// Guards the fields below. Jobs and page requests use the client at once, so every read and write of them takes
    /// this lock.
    /// </summary>
    private readonly object _stateGate = new();
    private DateTimeOffset? _forbiddenUntil;

    /// <summary>When the wait after a 429 ends; the next request clears it once it has waited.</summary>
    private DateTimeOffset? _rateLimitedUntil;

    /// <summary>When the last 429's wait ends, kept for <see cref="PausedUntil"/> after the next request cleared the other.</summary>
    private DateTimeOffset? _rateLimitPauseEnds;
    private MamAccountSnapshot? _lastAccountState;
    private DateTimeOffset? _lastSuccessAt;
    private MamRefusal? _lastRefusal;

    /// <summary>
    /// Raised when MAM refuses `mam_id` and calls are paused (403), blocks the account (406) or asks for fewer requests
    /// (429). A 403 that finds calls already paused raises nothing: one event per pause. Handlers run on the thread
    /// that made the call, so they must be quick, and one that throws is logged and skipped.
    /// </summary>
    public event Action<MamRefusalEvent>? Refused;

    /// <summary>
    /// Raised after each read of the account from MAM, with the read before it (null for the first). Handlers run on the
    /// thread that made the read, so they must be quick, and one that throws is logged and skipped.
    /// </summary>
    public event Action<MamAccountSnapshot?, MamAccountSnapshot>? AccountStateRead;

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A handler of a MAM event failed: {Error}", ex.Message);
        }
    }

    /// <summary>The account's numbers MAM last gave, with when; kept after the 60-second cache has run out. Null before the first read.</summary>
    public MamAccountSnapshot? LastAccountState
    {
        get
        {
            lock (_stateGate)
            {
                return _lastAccountState;
            }
        }
    }

    /// <summary>Until when MAM calls are paused after a 403 or a 429; null while they aren't.</summary>
    public DateTimeOffset? PausedUntil => CurrentPause()?.Until;

    /// <summary>Why MAM calls are paused; null while they aren't.</summary>
    public MamPauseReason? PauseReason => CurrentPause()?.Reason;

    /// <summary>When MAM last answered any request with success; null if it hasn't since Fetchshelf started.</summary>
    public DateTimeOffset? LastSuccessAt
    {
        get
        {
            lock (_stateGate)
            {
                return _lastSuccessAt;
            }
        }
    }

    /// <summary>The most recent time MAM refused the session (HTTP 403) or the account (HTTP 406), and what it said.</summary>
    public MamRefusal? LastRefusal
    {
        get
        {
            lock (_stateGate)
            {
                return _lastRefusal;
            }
        }
    }

    private (DateTimeOffset Until, MamPauseReason Reason)? CurrentPause()
    {
        var now = _timeProvider.GetUtcNow();
        lock (_stateGate)
        {
            // A refused mam_id outweighs a rate limit: it stays until mam_id is fixed or the pause runs out.
            if (_forbiddenUntil is { } forbidden && forbidden > now)
            {
                return (forbidden, MamPauseReason.Forbidden);
            }

            return _rateLimitPauseEnds is { } limited && limited > now ? (limited, MamPauseReason.RateLimited) : null;
        }
    }

    /// <summary>
    /// Reads account state (ratio, upload/download totals, wedges, unsat count/limit) from
    /// `GET /jsonLoad.php?snatch_summary=true`, caching the result for about 60 seconds.
    /// </summary>
    public Task<MamAccountState> GetAccountStateAsync(CancellationToken cancellationToken = default) =>
        ReadAccountStateAsync(useCache: true, cancellationToken);

    /// <summary>Reads the account state from MAM even when a read less than 60 seconds old is cached, as the Account page's Refresh does.</summary>
    public Task<MamAccountState> RefreshAccountStateAsync(CancellationToken cancellationToken = default) =>
        ReadAccountStateAsync(useCache: false, cancellationToken);

    private async Task<MamAccountState> ReadAccountStateAsync(bool useCache, CancellationToken cancellationToken)
    {
        await _accountStateLock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (useCache && LastAccountState is { } cached && now - cached.FetchedAt < AccountStateCacheDuration)
            {
                return cached.State;
            }

            var fresh = await FetchAccountStateAsync(cancellationToken);
            MamAccountSnapshot? previous;
            var current = new MamAccountSnapshot(fresh, now);
            lock (_stateGate)
            {
                previous = _lastAccountState;
                _lastAccountState = current;
            }

            Raise(() => AccountStateRead?.Invoke(previous, current));
            return fresh;
        }
        finally
        {
            _accountStateLock.Release();
        }
    }

    private async Task<MamAccountState> FetchAccountStateAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BuildUrl("/jsonLoad.php?snatch_summary=true")), cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseAccountState(document.RootElement);
    }

    internal static MamAccountState ParseAccountState(JsonElement root)
    {
        var summary = root.GetProperty("snatch_summary");
        var unsat = summary.GetProperty("unsat");

        return new MamAccountState
        {
            Ratio = MamJson.GetDouble(root.GetProperty("ratio")),
            UploadedBytes = MamJson.GetDouble(root.GetProperty("uploaded_bytes")),
            DownloadedBytes = MamJson.GetDouble(root.GetProperty("downloaded_bytes")),
            Wedges = MamJson.GetInt64(root.GetProperty("wedges")),
            Uid = MamJson.GetInt64(root.GetProperty("uid")),
            UnsatCount = MamJson.GetInt64(unsat.GetProperty("count")),
            UnsatLimit = MamJson.GetInt64(unsat.GetProperty("limit")),
            Username = Optional(root, "username", MamJson.GetString),
            ClassName = Optional(root, "classname", MamJson.GetString),
            BonusPoints = Optional(root, "seedbonus", e => (double?)MamJson.GetDouble(e)),
            Connectable = Optional(summary, "connectable", e => e.ValueKind switch
            {
                JsonValueKind.True => "yes",
                JsonValueKind.False => "no",
                _ => MamJson.GetString(e),
            }),
            SnatchKinds = ReadSnatchKinds(summary),
        };
    }

    /// <summary>
    /// Every object in the snatch summary with a count, read the same way whatever its key, so a kind MAM adds shows
    /// without a code change. Flat values such as `partial` are not kinds and are left out.
    /// </summary>
    private static List<MamSnatchKind> ReadSnatchKinds(JsonElement summary)
    {
        var kinds = new List<MamSnatchKind>();
        foreach (var entry in summary.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object
                || Optional(entry.Value, "count", e => (long?)MamJson.GetInt64(e)) is not { } count)
            {
                continue;
            }

            kinds.Add(new MamSnatchKind(
                entry.Name,
                Optional(entry.Value, "name", MamJson.GetString) ?? entry.Name,
                count,
                Optional(entry.Value, "size", e => (long?)MamJson.GetInt64(e))));
        }

        return kinds;
    }

    /// <summary>The property read with <paramref name="read"/>; null when it is missing, null, or can't be read.</summary>
    private static T? Optional<T>(JsonElement parent, string name, Func<JsonElement, T> read)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        try
        {
            return read(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            return default;
        }
    }

    /// <summary>
    /// Sends a request with the configured `mam_id` cookie attached. Any cookie MAM sets in the
    /// response is ignored. Backs off on HTTP 429 and throws on HTTP 406.
    /// An HTTP 403 throws <see cref="MamForbiddenException"/> and, unless <paramref name="pauseOnForbidden"/> is
    /// false, pauses all further MAM calls for <see cref="ForbiddenPause"/>. Torrent downloads opt out of the pause,
    /// since one refused download does not show that the session itself is dead.
    /// A request that gets no answer (the connection fails or times out) or HTTP 502, 503 or 504 is sent once more after
    /// <see cref="TransientRetryDelay"/>, unless <paramref name="retryTransient"/> is false: a download that spends a
    /// wedge must not be repeated, since the first attempt may have reached MAM. The request is built by
    /// <paramref name="createRequest"/> for each attempt, as a request message can only be sent once.
    /// </summary>
    internal async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken, bool pauseOnForbidden = true, bool retryTransient = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            var mayRetry = retryTransient && attempt == 1;
            using var request = createRequest();
            var response = await SendOnceAsync(request, cancellationToken, pauseOnForbidden, mayRetry);
            if (response is not null)
            {
                return response;
            }

            await _delay(TransientRetryDelay, cancellationToken);
        }
    }

    /// <summary>One attempt of <see cref="SendAsync"/>; null when it failed in a way worth one more try.</summary>
    private async Task<HttpResponseMessage?> SendOnceAsync(
        HttpRequestMessage request, CancellationToken cancellationToken, bool pauseOnForbidden, bool mayRetry)
    {
        ThrowIfPausedAfterForbidden();
        await ApplyRateLimitBackoffAsync(cancellationToken);

        if (!string.IsNullOrEmpty(options.MamId))
        {
            request.Headers.Add("Cookie", $"{CookieName}={options.MamId}");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (mayRetry && IsNoAnswer(ex, cancellationToken))
        {
            _logger.LogInformation(
                "MAM {Method} {Endpoint} failed ({Reason}), trying again in {Delay} s",
                request.Method, RedactDownloadHash(request.RequestUri?.AbsolutePath ?? "?"), DescribeNoAnswer(ex), TransientRetryDelay.TotalSeconds);
            return null;
        }

        if (mayRetry && (int)response.StatusCode is 502 or 503 or 504)
        {
            await LogFailedResponseAsync(request, response, cancellationToken);
            _logger.LogInformation("Trying again in {Delay} s", TransientRetryDelay.TotalSeconds);
            response.Dispose();
            return null;
        }

        if (response.IsSuccessStatusCode)
        {
            lock (_stateGate)
            {
                _lastSuccessAt = _timeProvider.GetUtcNow();
            }
        }
        else
        {
            await LogFailedResponseAsync(request, response, cancellationToken);
        }

        if ((int)response.StatusCode == 429)
        {
            var (limitedAt, limitedUntil) = (_timeProvider.GetUtcNow(), _timeProvider.GetUtcNow() + RateLimitBackoff);
            lock (_stateGate)
            {
                _rateLimitedUntil = _rateLimitPauseEnds = limitedUntil;
            }

            response.Dispose();
            Raise(() => Refused?.Invoke(new MamRefusalEvent(MamRefusalKind.RateLimited, limitedAt, limitedUntil)));
            throw new MamRateLimitedException("MAM rate limit hit (HTTP 429).");
        }

        if ((int)response.StatusCode == 406)
        {
            response.Dispose();
            const string blocked = "MAM account is temporarily blocked (HTTP 406).";
            RecordRefusal(blocked);
            Raise(() => Refused?.Invoke(new MamRefusalEvent(MamRefusalKind.Blocked, _timeProvider.GetUtcNow(), null)));
            throw new MamAccountBlockedException(blocked);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            response.Dispose();
            DateTimeOffset? pausedUntil = null;
            if (pauseOnForbidden)
            {
                pausedUntil = _timeProvider.GetUtcNow() + ForbiddenPause;
                lock (_stateGate)
                {
                    _forbiddenUntil = pausedUntil;
                }
            }

            var message = ForbiddenMessage(pausedUntil);
            RecordRefusal(message);
            if (pausedUntil is { } pausedAt)
            {
                Raise(() => Refused?.Invoke(new MamRefusalEvent(MamRefusalKind.Forbidden, _timeProvider.GetUtcNow(), pausedAt)));
            }

            throw new MamForbiddenException(message);
        }

        return response;
    }

    private void RecordRefusal(string message)
    {
        lock (_stateGate)
        {
            _lastRefusal = new MamRefusal(_timeProvider.GetUtcNow(), message);
        }
    }

    private void ThrowIfPausedAfterForbidden()
    {
        DateTimeOffset until;
        lock (_stateGate)
        {
            if (_forbiddenUntil is not { } forbiddenUntil)
            {
                return;
            }

            if (_timeProvider.GetUtcNow() >= forbiddenUntil)
            {
                _forbiddenUntil = null;
                return;
            }

            until = forbiddenUntil;
        }

        throw new MamForbiddenException(ForbiddenMessage(until));
    }

    private static string ForbiddenMessage(DateTimeOffset? pausedUntil)
    {
        const string cause = "MAM refused the request (HTTP 403). The mam_id may have been invalidated or be locked to a different IP address, or the account may be blocked.";
        return pausedUntil is { } until
            ? $"{cause} MAM calls are paused until {until:HH:mm} UTC; fix mam_id in config.toml and restart Fetchshelf to resume sooner."
            : cause;
    }

    /// <summary>
    /// Logs why a call failed. The body is only read for the statuses SendAsync turns into exceptions (and so disposes anyway):
    /// it tells a Cloudflare block page from a MAM message. Other failures are returned for the caller to read.
    /// </summary>
    private async Task LogFailedResponseAsync(HttpRequestMessage request, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "none";
        var endpoint = RedactDownloadHash(request.RequestUri?.AbsolutePath ?? "?");

        if (status is not (403 or 406 or 429))
        {
            _logger.LogWarning("MAM {Method} {Endpoint} returned HTTP {Status} ({ContentType})", request.Method, endpoint, status, contentType);
            return;
        }

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            body = "(body unavailable)";
        }

        var snippet = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (snippet.Length > LoggedBodyLength)
        {
            snippet = $"{snippet[..LoggedBodyLength]}...";
        }

        _logger.LogWarning(
            "MAM {Method} {Endpoint} returned HTTP {Status} ({ContentType}): {BodyStart}", request.Method, endpoint, status, contentType, snippet);
    }

    /// <summary>The hash in `/tor/download.php/&lt;hash&gt;` is a personal download token; keep it out of logs.</summary>
    private static string RedactDownloadHash(string path)
    {
        const string marker = "/download.php/";
        var index = path.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? path : $"{path[..(index + marker.Length)]}<hash>";
    }

    /// <summary>Waits out a prior 429's backoff window, if still active, before issuing another request.</summary>
    private async Task ApplyRateLimitBackoffAsync(CancellationToken cancellationToken)
    {
        TimeSpan remaining;
        lock (_stateGate)
        {
            if (_rateLimitedUntil is not { } blockedUntil)
            {
                return;
            }

            remaining = blockedUntil - _timeProvider.GetUtcNow();
            _rateLimitedUntil = null;
        }

        if (remaining > TimeSpan.Zero)
        {
            await _delay(remaining, cancellationToken);
        }
    }

    private string BuildUrl(string path) => $"{options.BaseUrl.TrimEnd('/')}{path}";
}
