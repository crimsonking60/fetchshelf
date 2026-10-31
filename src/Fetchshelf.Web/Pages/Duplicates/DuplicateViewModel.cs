using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Duplicates;

/// <summary>What a skipped torrent duplicates: a known torrent (linked to its page), a selection, or only a MaM id.</summary>
public sealed record DuplicateOfViewModel(string Text, string? TorrentId, string? MamLink);

/// <summary>Display model for one row on the Duplicates page.</summary>
public sealed record DuplicateViewModel(
    int Id, string Title, string Authors, string MamLink, DuplicateOfViewModel DuplicateOf, DateTimeOffset CreatedAt)
{
    public static DuplicateViewModel From(DuplicateTorrentEntity entity, DuplicateOfViewModel duplicateOf, string mamBaseUrl)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
        return new DuplicateViewModel(
            entity.Id, metadata.Title, string.Join(", ", metadata.Authors), MamTorrentLink(mamBaseUrl, entity.MamId), duplicateOf, entity.CreatedAt);
    }

    public static string MamTorrentLink(string mamBaseUrl, long mamId) => $"{mamBaseUrl.TrimEnd('/')}/t/{mamId}";
}
