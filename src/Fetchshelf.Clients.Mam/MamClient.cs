using System.Text.Json;

namespace Fetchshelf.Clients.Mam;

/// <summary>
/// Typed client for the MyAnonamouse (MaM) HTTP API.
/// </summary>
public sealed partial class MamClient(
    HttpClient httpClient,
    MamClientOptions options,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const string CookieName = "mam_id";
    private static readonly TimeSpan AccountStateCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to back off after a 429 before the next request.
    /// 60s is a conservative default for an unofficial API.
    /// </summary>
    private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Overridable so tests can prove politeness delays are requested without sleeping for the real interval.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((wait, ct) => Task.Delay(wait, timeProvider ?? TimeProvider.System, ct));

    private readonly SemaphoreSlim _accountStateLock = new(1, 1);
    private DateTimeOffset? _rateLimitedUntil;
    private (DateTimeOffset FetchedAt, MamAccountState Value)? _cachedAccountState;

    /// <summary>
    /// Verifies that the configured `mam_id` is accepted by MaM. The configured cookie is always
    /// the one sent; nothing is persisted or rotated.
    /// </summary>
    public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (!await CheckCookieAsync(cancellationToken))
        {
            throw new MamAuthenticationException("MaM authentication failed: the configured mam_id was rejected.");
        }
    }

    /// <summary>
    /// Reads account state (ratio, upload/download totals, wedges, unsat count/limit) from
    /// `GET /jsonLoad.php?snatch_summary=true`, caching the result for about 60 seconds
    /// (Fetchshelf-spec.md §5.1).
    /// </summary>
    public async Task<MamAccountState> GetAccountStateAsync(CancellationToken cancellationToken = default)
    {
        await _accountStateLock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_cachedAccountState is { } cached && now - cached.FetchedAt < AccountStateCacheDuration)
            {
                return cached.Value;
            }

            var fresh = await FetchAccountStateAsync(cancellationToken);
            _cachedAccountState = (now, fresh);
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
            new HttpRequestMessage(HttpMethod.Get, BuildUrl("/jsonLoad.php?snatch_summary=true")), cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var unsat = root.GetProperty("snatch_summary").GetProperty("unsat");

        return new MamAccountState
        {
            Ratio = MamJson.GetDouble(root.GetProperty("ratio")),
            UploadedBytes = MamJson.GetDouble(root.GetProperty("uploaded_bytes")),
            DownloadedBytes = MamJson.GetDouble(root.GetProperty("downloaded_bytes")),
            Wedges = MamJson.GetInt64(root.GetProperty("wedges")),
            Uid = MamJson.GetInt64(root.GetProperty("uid")),
            UnsatCount = MamJson.GetInt64(unsat.GetProperty("count")),
            UnsatLimit = MamJson.GetInt64(unsat.GetProperty("limit")),
        };
    }

    /// <summary>Validates the active cookie against `GET /json/checkCookie.php` (Fetchshelf-spec.md §5.1).</summary>
    private async Task<bool> CheckCookieAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, BuildUrl("/json/checkCookie.php")), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("\"Success\":true", StringComparison.Ordinal);
    }

    /// <summary>
    /// Sends a request with the configured `mam_id` cookie attached. Any cookie MaM sets in the
    /// response is ignored. Backs off on HTTP 429 and throws on HTTP 406 (Fetchshelf-spec.md §5.1).
    /// </summary>
    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await ApplyRateLimitBackoffAsync(cancellationToken);

        if (!string.IsNullOrEmpty(options.MamId))
        {
            request.Headers.Add("Cookie", $"{CookieName}={options.MamId}");
        }

        var response = await httpClient.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode == 429)
        {
            _rateLimitedUntil = _timeProvider.GetUtcNow() + RateLimitBackoff;
            response.Dispose();
            throw new MamRateLimitedException("MaM rate limit hit (HTTP 429).");
        }

        if ((int)response.StatusCode == 406)
        {
            response.Dispose();
            throw new MamAccountBlockedException("MaM account is temporarily blocked (HTTP 406).");
        }

        return response;
    }

    /// <summary>Waits out a prior 429's backoff window, if still active, before issuing another request.</summary>
    private async Task ApplyRateLimitBackoffAsync(CancellationToken cancellationToken)
    {
        if (_rateLimitedUntil is not { } blockedUntil)
        {
            return;
        }

        var remaining = blockedUntil - _timeProvider.GetUtcNow();
        _rateLimitedUntil = null;
        if (remaining > TimeSpan.Zero)
        {
            await _delay(remaining, cancellationToken);
        }
    }

    private string BuildUrl(string path) => $"{options.BaseUrl.TrimEnd('/')}{path}";
}
