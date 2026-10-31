namespace Fetchshelf.Core.Metadata;

/// <summary>Where a torrent's metadata came from.</summary>
public enum MetadataSource
{
    Mam,
    Manual,
}

/// <summary>
/// The metadata fields shared by every torrent Fetchshelf knows about. This is
/// a plain value object with no persistence or I/O dependency, used by the metadata-normalization
/// domain logic (title normalization, same-book test, cleaning) and mapped to/from storage by
/// Fetchshelf.Data.
/// </summary>
public sealed record TorrentMetadata
{
    public required long MamId { get; init; }

    public required VipStatus VipStatus { get; init; }

    public required MediaType MediaType { get; init; }

    public FictionCategory? FictionCategory { get; init; }

    public required LegacyCategory LegacyCategory { get; init; }

    public IReadOnlySet<GenreCategory> GenreCategories { get; init; } = new HashSet<GenreCategory>();

    public required Language Language { get; init; }

    public BookFlags Flags { get; init; } = BookFlags.None;

    public IReadOnlyList<string> FileTypes { get; init; } = [];

    public int FileCount { get; init; }

    public long TotalSizeBytes { get; init; }

    public required string Title { get; init; }

    public Edition? Edition { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public IReadOnlyList<string> Narrators { get; init; } = [];

    public IReadOnlyList<Series> Series { get; init; } = [];

    public required MetadataSource MetadataSource { get; init; }

    public required DateTimeOffset UploadTime { get; init; }

    /// <summary>
    /// Not one of the shared metadata fields; added for `metadata.json`. Absent until a
    /// metadata source (MAM lookup, or a future manual-edit extension) populates it.
    /// </summary>
    public string? Description { get; init; }

    public string? Isbn { get; init; }

    public string? Asin { get; init; }
}
