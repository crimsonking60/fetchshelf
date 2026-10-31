using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>Display model for one row on the Torrents page.</summary>
public sealed record TorrentListItemViewModel(
    string Id,
    string Title,
    string Authors,
    string Series,
    string MediaType,
    string Language,
    string? QBittorrentCategory,
    string? ClientStatus,
    string? LibraryMismatchState,
    DateTimeOffset CreatedAt)
{
    public static TorrentListItemViewModel From(TorrentEntity entity)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
        return new TorrentListItemViewModel(
            entity.Id,
            metadata.Title,
            string.Join(", ", metadata.Authors),
            string.Join(", ", metadata.Series.Select(s => s.Name)),
            metadata.MediaType.ToString(),
            metadata.Language.ToString(),
            entity.QBittorrentCategory,
            entity.ClientStatus,
            entity.LibraryMismatchState,
            entity.CreatedAt);
    }
}
