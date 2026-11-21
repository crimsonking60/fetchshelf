namespace Fetchshelf.Clients.Mam;

public sealed partial class MamClient
{
    /// <summary>
    /// The least time between two `.torrent` downloads. MAM refuses a download with a 429 once about ten have been made
    /// within ten seconds, so one a second hits that limit on a long queue; two seconds stays well under it.
    /// </summary>
    internal static readonly TimeSpan DownloadPoliteness = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _downloadPoliteness = new(1, 1);
    private DateTimeOffset? _lastDownloadSentAt;

    /// <summary>
    /// Downloads a torrent's `.torrent` file from `GET /tor/download.php/&lt;dlHash&gt;?tid=&lt;mamId&gt;[&amp;fl]`,
    /// applying the <see cref="DownloadPoliteness"/> wait between downloads. `tid` is required even though the hash is
    /// in the path; `fl` spends a freeleech wedge on the download.
    /// </summary>
    public async Task<byte[]> DownloadTorrentFileAsync(string dlHash, long mamId, bool wedge = false, CancellationToken cancellationToken = default)
    {
        await ApplyDownloadPolitenessAsync(cancellationToken);

        var url = BuildUrl($"/tor/download.php/{dlHash}?tid={mamId}{(wedge ? "&fl" : "")}");
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken, pauseOnForbidden: false, retryTransient: !wedge);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task ApplyDownloadPolitenessAsync(CancellationToken cancellationToken)
    {
        await _downloadPoliteness.WaitAsync(cancellationToken);
        try
        {
            if (_lastDownloadSentAt is { } lastSent)
            {
                var elapsed = _timeProvider.GetUtcNow() - lastSent;
                var remaining = DownloadPoliteness - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await _delay(remaining, cancellationToken);
                }
            }

            _lastDownloadSentAt = _timeProvider.GetUtcNow();
        }
        finally
        {
            _downloadPoliteness.Release();
        }
    }
}
