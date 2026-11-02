using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>Reads an `[[autograb]]` entry's `type` as string-or-`{ uploader = &lt;id&gt; }` mapping.</summary>
public sealed class AutograbTypeYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(AutograbType);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return scalar.Value.ToLowerInvariant() switch
            {
                "bookmarks" => AutograbType.Bookmarks,
                "freeleech" => AutograbType.Freeleech,
                "new" or "torrents" => AutograbType.New,
                "mine" => AutograbType.Mine,
                _ => throw new ConfigurationException($"Invalid configuration: unrecognized autograb type '{scalar.Value}'."),
            };
        }

        parser.Consume<MappingStart>();
        var key = parser.Consume<Scalar>();
        if (!string.Equals(key.Value, "uploader", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException($"Invalid configuration: unrecognized autograb type key '{key.Value}'.");
        }

        var value = parser.Consume<Scalar>();
        parser.Consume<MappingEnd>();
        if (!long.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var uploaderId))
        {
            throw new ConfigurationException(
                $"Invalid configuration: autograb type 'uploader' needs the uploader's MAM user id, a number, not '{value.Value}'.");
        }

        return AutograbType.Uploader(uploaderId);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Fetchshelf never writes its own configuration back out as YAML.");
}
