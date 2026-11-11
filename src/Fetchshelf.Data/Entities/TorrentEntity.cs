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
}
