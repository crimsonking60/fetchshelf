namespace Fetchshelf.Clients.QBittorrent;

/// <summary>Produces one independently-configured <see cref="QBittorrentClient"/> per connection entry.</summary>
public sealed class QBittorrentClientFactory(IHttpClientFactory httpClientFactory)
{
    public QBittorrentClient Create(QBittorrentClientOptions options)
    {
        var httpClient = httpClientFactory.CreateClient("qbittorrent");
        return new QBittorrentClient(httpClient, options);
    }
}
