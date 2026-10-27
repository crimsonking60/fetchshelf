using System.Text.Json;
using System.Text.Json.Serialization;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Serializes <see cref="TorrentMetadata"/> for storage in <c>TorrentEntity.MetadataJson</c>.</summary>
public static class TorrentMetadataSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new ReadOnlySetConverter<GenreCategory>() },
    };

    public static string Serialize(TorrentMetadata metadata) => JsonSerializer.Serialize(metadata, Options);

    public static TorrentMetadata Deserialize(string json) =>
        JsonSerializer.Deserialize<TorrentMetadata>(json, Options)
        ?? throw new InvalidOperationException("Stored torrent metadata JSON deserialized to null.");
}

/// <summary>
/// <see cref="System.Text.Json"/> has no built-in support for deserializing into
/// <see cref="IReadOnlySet{T}"/>; this reads the JSON array into a <see cref="HashSet{T}"/>.
/// </summary>
internal sealed class ReadOnlySetConverter<T> : JsonConverter<IReadOnlySet<T>>
    where T : notnull
{
    public override IReadOnlySet<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<HashSet<T>>(ref reader, options);

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}
