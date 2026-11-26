using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Web.Pages.Duplicates;

namespace Fetchshelf.Web.Pages.Queue;

/// <summary>
/// What Fetchshelf stored about a selection when it was picked, for the dialog its title opens on the Queue page.
/// Nothing is asked of MAM: the link to the torrent's MAM page is there for the rest.
/// </summary>
public sealed record SelectionDetailsViewModel(
    SelectedTorrentViewModel Selection,
    TorrentMetadata Metadata,
    string MamLink,
    bool StartedWithWedge,
    bool GenresOn)
{
    /// <summary>The description as plain text; MAM sends it as HTML.</summary>
    public string? Description => DescriptionText.From(Metadata.Description);

    /// <summary>Each series with its number, when it has one.</summary>
    public IReadOnlyList<string> Series =>
        Metadata.Series.Select(s => LibraryMetadataJsonBuilder.FormatSequence(s) is { } number ? $"{s.Name} #{number}" : s.Name).ToList();

    /// <summary>The genres by MAM's names, A-Z, for <see cref="GenresOn"/>.</summary>
    public IReadOnlyList<string> GenreLabelList =>
        Metadata.GenreCategories.Select(GenreLabels.Label).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>MAM's categories as names, with `genres` off.</summary>
    public IReadOnlyList<string> Categories => LibraryMetadataJsonBuilder.BuildGenres(Metadata);

    public string Vip => Metadata.VipStatus.Kind switch
    {
        VipStatusKind.NotVip => "No",
        VipStatusKind.Permanent => "Yes, permanent",
        VipStatusKind.Temporary => $"Yes, until {Metadata.VipStatus.ExpiresOn:yyyy-MM-dd}",
        _ => "Unknown",
    };

    public static SelectionDetailsViewModel From(SelectedTorrentEntity entity, string mamBaseUrl, bool genresOn) => new(
        SelectedTorrentViewModel.From(entity),
        TorrentMetadataSerializer.Deserialize(entity.MetadataJson),
        DuplicateViewModel.MamTorrentLink(mamBaseUrl, entity.MamId),
        entity.StartedWithWedge,
        genresOn);
}
