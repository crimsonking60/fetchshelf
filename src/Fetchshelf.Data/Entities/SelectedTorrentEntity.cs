namespace Fetchshelf.Data.Entities;

/// <summary>A torrent that has passed filters and is queued for download.</summary>
public sealed class SelectedTorrentEntity
{
    public int Id { get; set; }

    public required long MamId { get; set; }

    public required string DownloadLink { get; set; }

    public required string CostDecision { get; set; }

    public string? Category { get; set; }

    public string? TagsJson { get; set; }

    public string? GrabberName { get; set; }

    public required string MetadataJson { get; set; }

    public required string NormalizedTitleKey { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }
}
