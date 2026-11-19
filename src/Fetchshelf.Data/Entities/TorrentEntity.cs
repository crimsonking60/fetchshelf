namespace Fetchshelf.Data.Entities;

/// <summary>
/// A torrent Fetchshelf knows about. <see cref="MetadataJson"/> holds the
/// serialized shared metadata fields (<c>Fetchshelf.Core.Metadata.TorrentMetadata</c>); the
/// library-organizer fields below are schema placeholders populated by a later change, not by
/// Foundation.
/// </summary>
public sealed class TorrentEntity
{
    /// <summary>Info-hash; for metadata-only entries, a string derived from the MAM id.</summary>
    public required string Id { get; set; }

    public required long MamId { get; set; }

    public required string NormalizedTitleKey { get; set; }

    public required string Title { get; set; }

    public required string MetadataJson { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public string? LibraryPath { get; set; }

    public string? LinkedFilesJson { get; set; }

    public string? ChosenAudioExtension { get; set; }

    public string? ChosenEbookExtension { get; set; }

    public string? QBittorrentCategory { get; set; }

    public string? LinkerName { get; set; }

    public string? AbsId { get; set; }

    public string? ReplacedWithTorrentId { get; set; }

    public DateTimeOffset? ReplacedAt { get; set; }

    /// <summary>When its files were taken out of the library by hand; the Link job then leaves it alone.</summary>
    public DateTimeOffset? UnlinkedAt { get; set; }

    public string? LibraryMismatchState { get; set; }

    /// <summary>qBittorrent's state for the torrent, or one of <c>ClientStatuses</c>' own values.</summary>
    public string? ClientStatus { get; set; }

    /// <summary>When an autograb with `mark_removed` found that MAM no longer has the torrent. qBittorrent updates leave it alone.</summary>
    public DateTimeOffset? RemovedFromMamAt { get; set; }

    /// <summary>When an autograb with `mark_removed` last asked MAM whether it still has the torrent.</summary>
    public DateTimeOffset? MamCheckedAt { get; set; }

    /// <summary>
    /// The metadata's language (<c>Language</c> enum name), kept in step with <see cref="MetadataJson"/> by
    /// <see cref="FetchshelfDbContext"/> so it can be filtered on; null when the metadata can't be read.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>The metadata's media type (<c>MediaType</c> enum name), kept in step like <see cref="Language"/>.</summary>
    public string? MediaType { get; set; }

    /// <summary>When the Upgrade job last searched MAM for a better copy of this one; null when it never has.</summary>
    public DateTimeOffset? UpgradeSearchedAt { get; set; }

    /// <summary>
    /// How the torrent looks against MAM's seeding requirement (<c>SeedingState</c> enum name), as the Seeding job last
    /// estimated it from qBittorrent; null when it never has.
    /// </summary>
    public string? SeedingState { get; set; }

    /// <summary>How long it had seeded at that estimate: qBittorrent's <c>seeding_time</c>, in seconds.</summary>
    public long? SeedingSeconds { get; set; }

    /// <summary>When the seeding period started: when qBittorrent added the torrent.</summary>
    public DateTimeOffset? SeedingSince { get; set; }

    /// <summary>When the Seeding job last estimated it.</summary>
    public DateTimeOffset? SeedingCheckedAt { get; set; }

    /// <summary>The Clean job retired this copy but held `on_cleaned` back while it looked unsatisfied; the Seeding job applies it once it is satisfied.</summary>
    public bool OnCleanedHeld { get; set; }

    /// <summary>
    /// When the Link job last settled this book's cover (`covers_from_mam`): wrote one, found the folder has an image, or
    /// MAM had none. Null until then; linking the torrent again, or Refresh, sets it back to null so it is looked at again.
    /// </summary>
    public DateTimeOffset? CoverCheckedAt { get; set; }

    /// <summary>
    /// For a book of a split collection: the collection's record, the one qBittorrent and MAM know. Null for any other
    /// record. A book's id is <see cref="Core.Organizer.CollectionBookId"/>'s.
    /// </summary>
    public string? CollectionTorrentId { get; set; }

    /// <summary>For a collection linked as its books: how many book records it has. Null for any other record.</summary>
    public int? CollectionBookCount { get; set; }
}
