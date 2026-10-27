using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One `grab` block within a `[[goodreads_list]]`/`[[notion_list]]` entry, Fetchshelf-spec.md §7.8: a cost
/// plus the shared torrent filter. Inherits the shared filter's fields directly as its own top-level
/// YAML keys, matching every other filter-bearing config section's flattened shape.
/// </summary>
public sealed class GrabEntry : SharedFilterConfig
{
    public ConfiguredCost Cost { get; set; } = ConfiguredCost.Free;
}
