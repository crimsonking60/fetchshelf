using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>Display model for one row on the Torrents page.</summary>
public sealed record TorrentListItemViewModel(
    string Id,
    string Title,
    IReadOnlyList<NameLink> Authors,
    IReadOnlyList<NameLink> Series,
    string MediaType,
    string Language,
    string? QBittorrentCategory,
    string? ClientStatus,
    bool RemovedFromMam,
    string? LibraryMismatchState,
    bool Unlinked,
    DateTimeOffset CreatedAt)
{
    /// <summary>"mp3; m4b preferred" when a better format of this copy is possible (<see cref="Torrents.UpgradeNote"/>).</summary>
    public string? UpgradeNote { get; init; }

    /// <param name="metadata">The entity's metadata when the caller has already read it, so it isn't read twice.</param>
    public static TorrentListItemViewModel From(TorrentEntity entity, TorrentMetadata? metadata = null)
    {
        metadata ??= TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
        return new TorrentListItemViewModel(
            entity.Id,
            metadata.Title,
            NameLink.Authors(metadata),
            NameLink.Series(metadata),
            metadata.MediaType.ToString(),
            metadata.Language.ToString(),
            entity.QBittorrentCategory,
            entity.ClientStatus,
            entity.RemovedFromMamAt is not null,
            entity.LibraryMismatchState,
            entity.UnlinkedAt is not null,
            entity.CreatedAt);
    }
}

/// <summary>
/// An author, narrator or series name with the key the Torrents page filters it by. Every name in the metadata is
/// kept, as written; one whose key is empty (no letters or digits, such as "???") is shown without a link.
/// </summary>
public sealed record NameLink(string Name, string Key)
{
    public static IReadOnlyList<NameLink> Authors(TorrentMetadata metadata) => People(metadata.Authors);

    public static IReadOnlyList<NameLink> Narrators(TorrentMetadata metadata) => People(metadata.Narrators);

    /// <summary>Each series once, in the metadata's order.</summary>
    public static IReadOnlyList<NameLink> Series(TorrentMetadata metadata) =>
        (metadata.Series ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s?.Name))
            .Select(s => new NameLink(s.Name.Trim(), SeriesNameKey.Of(s.Name)))
            .DistinctBy(link => link.Key.Length > 0 ? link.Key : link.Name)
            .ToList();

    private static List<NameLink> People(IReadOnlyList<string>? names) =>
        (names ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new NameLink(name.Trim(), PersonNameKey.Of(name)))
            .ToList();
}

/// <summary>Names to show as Torrents page filter links: authors, narrators or series.</summary>
/// <param name="Parameter">The filter's query parameter: "Author", "Narrator" or "Series".</param>
/// <param name="Query">The list's current query, kept by the links; null for a plain filter on the name alone.</param>
public sealed record NameLinksViewModel(IReadOnlyList<NameLink> Names, string Parameter, TorrentListQuery? Query = null)
{
    public Dictionary<string, string> RouteValuesFor(NameLink name)
    {
        var values = Query?.ToRouteValues() ?? [];
        values[Parameter] = name.Key;
        return values;
    }
}
