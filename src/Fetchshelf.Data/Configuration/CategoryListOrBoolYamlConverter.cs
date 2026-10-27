using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Fetchshelf.Data.Configuration;

/// <summary>Reads a `categories.audio`/`ebook`/`musicology`/`radio` YAML value as bool-or-sequence.</summary>
public sealed class CategoryListOrBoolYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(CategoryListOrBool);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return bool.Parse(scalar.Value) ? CategoryListOrBool.All : CategoryListOrBool.None;
        }

        parser.Consume<SequenceStart>();
        var names = new List<string>();
        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            names.Add(parser.Consume<Scalar>().Value);
        }

        return CategoryListOrBool.Of(names);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
        throw new NotSupportedException("Fetchshelf never writes its own configuration back out as YAML.");
}
