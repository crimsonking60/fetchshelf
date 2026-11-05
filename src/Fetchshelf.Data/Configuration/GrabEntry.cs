using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One `grab` block within a `[[hardcover_list]]` entry: a cost
/// plus the shared torrent filter. Inherits the shared filter's fields directly as its own top-level
/// YAML keys, matching every other filter-bearing config section's flattened shape.
/// </summary>
public sealed class GrabEntry : SharedFilterConfig
{
    public ConfiguredCost Cost { get; set; } = ConfiguredCost.Free;
}
