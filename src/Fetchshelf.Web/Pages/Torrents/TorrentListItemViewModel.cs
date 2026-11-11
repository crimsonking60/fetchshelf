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
    public static TorrentListItemViewModel From(TorrentEntity entity)
    {
        var metadata = TorrentMetadataSerializer.Deserialize(entity.MetadataJson);
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

/// <summary>An author, narrator or series name with the key the Torrents page filters it by.</summary>
public sealed record NameLink(string Name, string Key)
{
    public static IReadOnlyList<NameLink> Authors(TorrentMetadata metadata) => People(metadata, PersonRole.Author);

    public static IReadOnlyList<NameLink> Narrators(TorrentMetadata metadata) => People(metadata, PersonRole.Narrator);

    /// <summary>Each series once, in the metadata's order.</summary>
    public static IReadOnlyList<NameLink> Series(TorrentMetadata metadata) =>
        TorrentProjection.From(metadata).Series.DistinctBy(s => s.NameKey).Select(s => new NameLink(s.Name, s.NameKey)).ToList();

    private static List<NameLink> People(TorrentMetadata metadata, PersonRole role) =>
        TorrentProjection.From(metadata).People.Where(p => p.Role == role).Select(p => new NameLink(p.Name, p.NameKey)).ToList();
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
