using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>The `[search]` section.</summary>
public sealed class SearchSettings
{
    public string? WedgeOver { get; set; }

    /// <summary>
    /// `covers`: show a cover in a search result's details. Not from MAM, whose cover server refuses Fetchshelf since
    /// 2026-11-22, but from the cover sources the Link job uses; with none, nothing is shown. Off by default.
    /// </summary>
    public bool Covers { get; set; }

    /// <summary><see cref="WedgeOver"/> in bytes, read once when the configuration is loaded.</summary>
    [YamlIgnore]
    public long? WedgeOverBytes { get; set; }
}
