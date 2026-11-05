using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Jobs;

namespace Fetchshelf.Web;

/// <summary>
/// Which torrents qBittorrent has, by MAM id (from the MAM comment in each .torrent file) and by info hash, with
/// each one's category. <see cref="Loaded"/> is false when qBittorrent could not be asked.
/// </summary>
public sealed record QBittorrentPresence(
    IReadOnlyDictionary<long, string> CategoryByMamId, IReadOnlyDictionary<string, string> CategoryByHash, bool Loaded)
{
    public static readonly QBittorrentPresence Unknown =
        new(new Dictionary<long, string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Loaded: false);

    public static QBittorrentPresence From(IEnumerable<QBittorrentTorrent> torrents)
    {
        var byMamId = new Dictionary<long, string>();
        var byHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var torrent in torrents)
        {
            byHash.TryAdd(torrent.Hash, torrent.Category);
            if (torrent.MamId is { } mamId)
            {
                byMamId.TryAdd(mamId, torrent.Category);
            }
        }

        return new QBittorrentPresence(byMamId, byHash, Loaded: true);
    }

    /// <summary>The torrent's category if qBittorrent has it ("" for none), found by MAM id or else by info hash.</summary>
    public string? CategoryOf(long mamId, IEnumerable<string> knownHashes) =>
        CategoryByMamId.TryGetValue(mamId, out var category)
            ? category
            : knownHashes.Select(h => CategoryByHash.GetValueOrDefault(h)).FirstOrDefault(c => c is not null);
}

/// <summary>
/// Keeps <see cref="QBittorrentPresence"/> for the Search page. qBittorrent's full torrent list can be megabytes and
/// take a good part of a second, so it is read at most every <see cref="MaxAge"/>, in the background: a search only
/// waits for it when nothing has been read yet, and then not longer than <see cref="FirstReadTimeout"/>.
/// </summary>
public sealed class QBittorrentPresenceCache(
    FetchshelfSettings settings, QBittorrentClientFactory factory, ILogger<QBittorrentPresenceCache> logger, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan FirstReadTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private QBittorrentPresence? _current;
    private DateTimeOffset _readAt;
    private Task? _refresh;

    public async Task<QBittorrentPresence> GetAsync()
    {
        Task? refresh;
        lock (_lock)
        {
            var stale = _current is null || _time.GetUtcNow() - _readAt > MaxAge;
            if (stale && _refresh is null)
            {
                _refresh = Task.Run(RefreshAsync);
            }

            if (_current is not null)
            {
                return _current;
            }

            refresh = _refresh;
        }

        try
        {
            await refresh!.WaitAsync(FirstReadTimeout);
        }
        catch (TimeoutException)
        {
            // Still reading; this search goes without, the next one finds the list ready.
        }

        lock (_lock)
        {
            return _current ?? QBittorrentPresence.Unknown;
        }
    }

    private async Task RefreshAsync()
    {
        QBittorrentPresence presence;
        try
        {
            if (settings.QBittorrent.Count == 0)
            {
                throw new InvalidOperationException("no [[qbittorrent]] is configured");
            }

            var torrents = new List<QBittorrentTorrent>();
            foreach (var (_, client) in QBittorrentConnections.Build(settings, factory))
            {
                torrents.AddRange(await client.ListTorrentsAsync());
            }

            presence = QBittorrentPresence.From(torrents);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not read the torrent list from qBittorrent for the Search page: {Error}", ex.Message);
            presence = QBittorrentPresence.Unknown;
        }

        lock (_lock)
        {
            _current = presence;
            _readAt = _time.GetUtcNow();
            _refresh = null;
        }
    }
}
