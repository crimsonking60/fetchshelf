namespace Fetchshelf.Data.Entities;

/// <summary>An imported Goodreads or Notion list, Fetchshelf-spec.md §3.2, §7.8.</summary>
public sealed class ListEntity
{
    /// <summary>E.g. "&lt;goodreads-user-id&gt;:&lt;shelf&gt;" for a Goodreads list (§7.8).</summary>
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string ListType { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? BuildDate { get; set; }
}

/// <summary>
/// One book within an imported Goodreads list, with per-side (audio/ebook) search status,
/// Fetchshelf-spec.md §3.2, §7.8. Keyed by the owning list plus the feed item's own guid, so a re-import
/// updates the same row rather than creating a duplicate. Each side is flattened into its own
/// mam-id/status/timestamp columns (matching <see cref="SelectedTorrentEntity"/>'s own flattened
/// status/timing fields) rather than nested owned types.
/// </summary>
public sealed class ListItemEntity
{
    public required string ListId { get; set; }

    /// <summary>The Goodreads feed item's guid.</summary>
    public required string Guid { get; set; }

    public required string BookTitle { get; set; }

    /// <summary>JSON array of author names.</summary>
    public required string AuthorsJson { get; set; }

    public string? SeriesName { get; set; }

    public double? SeriesNumber { get; set; }

    public required string CoverUrl { get; set; }

    public string? BookUrl { get; set; }

    public string? Isbn { get; set; }

    /// <summary>"Audio" or "Ebook" when the list configures a preferred format, else null (Fetchshelf-spec.md §7.8).</summary>
    public string? PreferFormat { get; set; }

    public bool AllowAudio { get; set; }

    public bool AllowEbook { get; set; }

    public long? AudioMamId { get; set; }

    /// <summary>One of Selected/Wanted/NotWanted/Existing; null when the audio side has not been resolved at all.</summary>
    public string? AudioStatus { get; set; }

    public DateTimeOffset? AudioAt { get; set; }

    public long? EbookMamId { get; set; }

    public string? EbookStatus { get; set; }

    public DateTimeOffset? EbookAt { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? MarkedDoneAt { get; set; }
}
