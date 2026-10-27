namespace Fetchshelf.Data.Configuration;

/// <summary>An optional category plus tags to apply in qBittorrent, Fetchshelf-spec.md §4.2.</summary>
public sealed class QBittorrentCategoryAndTags
{
    public string? Category { get; set; }

    public List<string> Tags { get; set; } = [];
}
