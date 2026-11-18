namespace Fetchshelf.Data.Entities;

/// <summary>A torrent that has passed filters and is queued for download.</summary>
public sealed class SelectedTorrentEntity
{
    public int Id { get; set; }

    public required long MamId { get; set; }

    public required string DownloadLink { get; set; }

    public required string CostDecision { get; set; }

    public string? Category { get; set; }

    /// <summary>The `name` of the `[[qbittorrent]]` the Download job adds it to; null means the first one.</summary>
    public string? ClientName { get; set; }

    /// <summary>The folder qBittorrent saves it in, as qBittorrent sees it; null leaves it to qBittorrent.</summary>
    public string? SavePath { get; set; }

    public string? TagsJson { get; set; }

    public string? GrabberName { get; set; }

    public required string MetadataJson { get; set; }

    public required string NormalizedTitleKey { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Whether the Download job started it with a freeleech wedge, so it costs no ratio while it downloads.</summary>
    public bool StartedWithWedge { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }
}
