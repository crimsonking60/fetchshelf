using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// A torrent as seen by the shared filter: <see cref="TorrentMetadata"/> plus the fields only a live
/// MaM search result carries (uploader, peer counts), Fetchshelf-spec.md §4.3/§5.1. Built once per search
/// result and discarded - never persisted.
/// </summary>
public sealed record TorrentFilterCandidate
{
    public required TorrentMetadata Metadata { get; init; }

    public required string UploaderName { get; init; }

    public required long Seeders { get; init; }

    public required long Leechers { get; init; }

    public required long TimesCompleted { get; init; }
}
