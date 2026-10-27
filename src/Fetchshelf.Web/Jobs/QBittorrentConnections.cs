using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Web.Jobs;

/// <summary>Builds one client per configured `[[qbittorrent]]` connection.</summary>
public static class QBittorrentConnections
{
    public static List<(QBittorrentConnection Connection, QBittorrentClient Client)> Build(
        FetchshelfSettings settings, QBittorrentClientFactory factory) =>
        settings.QBittorrent
            .Select(c => (c, factory.Create(new QBittorrentClientOptions(c.Url, c.Username, c.Password))))
            .ToList();
}
