using Fetchshelf.Core.AutoDownloader;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>Reads a `cost` value: `free`, `wedge`, `try_wedge`, `ratio` (alias `all`), `metadata_only`, `metadata_only_add`.</summary>
public sealed class ConfiguredCostYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(ConfiguredCost);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        return scalar.Value.ToLowerInvariant() switch
        {
            "free" => ConfiguredCost.Free,
            "wedge" => ConfiguredCost.Wedge,
            "try_wedge" => ConfiguredCost.TryWedge,
            "ratio" or "all" => ConfiguredCost.Ratio,
            "metadata_only" => ConfiguredCost.MetadataOnly,
            "metadata_only_add" => ConfiguredCost.MetadataOnlyAdd,
            _ => throw new ConfigurationException($"Invalid configuration: unrecognized cost '{scalar.Value}'."),
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Fetchshelf never writes its own configuration back out as YAML.");
}
