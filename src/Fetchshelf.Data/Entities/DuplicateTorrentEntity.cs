namespace Fetchshelf.Data.Entities;

/// <summary>
/// A candidate skipped because an equal-or-better torrent for the same book already exists.
/// </summary>
public sealed class DuplicateTorrentEntity
{
    public int Id { get; set; }

    public required long MamId { get; set; }

    public required string MetadataJson { get; set; }

    public required string DuplicateOfTorrentId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
