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

    public string? LibraryMismatchState { get; set; }

    public string? ClientStatus { get; set; }
}
