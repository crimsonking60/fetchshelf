namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Everything needed to recreate a `SelectedTorrentEntity` from a Grabber-stage error, so the
/// Errors page's "restore the selection" action doesn't lose the original
/// cost/category/tags/grabber when `DownloadRunner` removes a selection it couldn't download.
/// Serialized into `ErroredTorrentEntity.DisplayMetadataJson`.
/// </summary>
public sealed record GrabberErrorRestorePayload
{
    public required long MamId { get; init; }

    public required string DownloadLink { get; init; }

    public required string CostDecision { get; init; }

    public string? Category { get; init; }

    public string? TagsJson { get; init; }

    public string? GrabberName { get; init; }

    public required string MetadataJson { get; init; }

    public required string NormalizedTitleKey { get; init; }
}
