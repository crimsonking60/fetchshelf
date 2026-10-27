using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// One `[[goodreads_list]]` entry, Fetchshelf-spec.md §4.2, §7.8. List id is derived from <see cref="Url"/>
/// (the Goodreads user id and `shelf` query parameter), not stored separately.
/// </summary>
public sealed class GoodreadsListEntry
{
    public string Url { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>Restricted to <see cref="FilterMainType.Audio"/>/<see cref="FilterMainType.Ebook"/> (enforced by `ConfigLoader`).</summary>
    public FilterMainType? PreferFormat { get; set; }

    public List<GrabEntry> Grab { get; set; } = [];

    public int? SearchInterval { get; set; }

    public int? UnsatBuffer { get; set; }

    public int? WedgeBuffer { get; set; }

    public bool DryRun { get; set; }
}
