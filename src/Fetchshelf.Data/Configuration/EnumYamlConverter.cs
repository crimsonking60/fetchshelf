using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fetchshelf.Data.Configuration;

/// <summary>
/// Reads a setting whose values are an enum's (<c>method</c>, <c>folder_names</c>, <c>status</c>, ...) by their
/// underscored names, case-insensitively. A value that isn't one of them is refused with the names it can be, where
/// YamlDotNet would only say "Exception during deserialization".
/// </summary>
public sealed class EnumYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type.IsEnum;

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        foreach (var name in Enum.GetNames(type))
        {
            if (string.Equals(scalar.Value, UnderscoredNamingConvention.Instance.Apply(name), StringComparison.OrdinalIgnoreCase)
                || string.Equals(scalar.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse(type, name);
            }
        }

        throw new ConfigurationException($"Invalid configuration: '{scalar.Value}' is not one of {NamesOf(type)}.");
    }

    /// <summary><paramref name="value"/> as it is written in the configuration: <c>hardlink_or_copy</c>.</summary>
    public static string NameOf(Enum value) => UnderscoredNamingConvention.Instance.Apply(value.ToString());

    /// <summary>The values a setting of <paramref name="type"/> can have, as they are written in the configuration.</summary>
    public static string NamesOf(Type type) =>
        string.Join(", ", Enum.GetNames(type).Select(UnderscoredNamingConvention.Instance.Apply));

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Fetchshelf never writes its own configuration back out as YAML.");
}
