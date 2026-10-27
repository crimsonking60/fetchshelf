namespace Fetchshelf.Clients.Mam;

public sealed partial class MamClient
{
    private static readonly TimeSpan DownloadPoliteness = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _downloadPoliteness = new(1, 1);
    private DateTimeOffset? _lastDownloadSentAt;

    /// <summary>
    /// Downloads a torrent's `.torrent` file from `GET /tor/download.php/&lt;dlHash&gt;?tid=&lt;mamId&gt;[&amp;fl]`,
    /// applying the 1s-between-downloads politeness rule. `tid` is required even though the hash is
    /// in the path; `fl` spends a freeleech wedge on the download (Fetchshelf-spec.md §5.1, confirmed
    /// against the original Rust implementation's `torrent_file_url`).
    /// </summary>
    public async Task<byte[]> DownloadTorrentFileAsync(string dlHash, long mamId, bool wedge = false, CancellationToken cancellationToken = default)
    {
        await ApplyDownloadPolitenessAsync(cancellationToken);

        var url = BuildUrl($"/tor/download.php/{dlHash}?tid={mamId}{(wedge ? "&fl" : "")}");
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
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
