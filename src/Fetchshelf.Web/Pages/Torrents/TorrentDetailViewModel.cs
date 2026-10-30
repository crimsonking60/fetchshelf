using Fetchshelf.Clients.QBittorrent;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>Display model for the Torrent detail page, Fetchshelf-spec.md §10.3.</summary>
public sealed record TorrentDetailViewModel(
    string Id,
    TorrentMetadata Metadata,
    string? LibraryPath,
    IReadOnlyList<LinkedFileRecord> LinkedFiles,
    string? LibraryMismatchState,
    string? ReplacedWithTorrentId,
    IReadOnlyList<EventEntity> Events,
    QBittorrentTorrent? ClientState,
    string? AbsLink,
    string? MamLink)
{
    /// <param name="absUrl">The configured `[audiobookshelf].url`, or null when not configured.</param>
    /// <param name="mamBaseUrl">MaM's base URL; torrents without a MaM id (0) get no MaM link.</param>
    public static TorrentDetailViewModel From(
        TorrentEntity entity, IReadOnlyList<EventEntity> events, QBittorrentTorrent? clientState, string? absUrl, string mamBaseUrl) => new(
        entity.Id,
        TorrentMetadataSerializer.Deserialize(entity.MetadataJson),
        entity.LibraryPath,
        DeserializeLinkedFiles(entity.LinkedFilesJson),
        entity.LibraryMismatchState,
        entity.ReplacedWithTorrentId,
        events,
        clientState,
        entity.AbsId is not null && absUrl is not null ? $"{absUrl.TrimEnd('/')}/audiobookshelf/item/{entity.AbsId}" : null,
        entity.MamId > 0 ? $"{mamBaseUrl.TrimEnd('/')}/t/{entity.MamId}" : null);

    private static IReadOnlyList<LinkedFileRecord> DeserializeLinkedFiles(string? json) =>
        json is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<LinkedFileRecord>>(json) ?? [];
}
