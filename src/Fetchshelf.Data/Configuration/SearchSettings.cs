using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>The `[search]` section.</summary>
public sealed class SearchSettings
{
    public string? WedgeOver { get; set; }

    /// <summary><see cref="WedgeOver"/> in bytes, read once when the configuration is loaded.</summary>
    [YamlIgnore]
    public long? WedgeOverBytes { get; set; }
}
