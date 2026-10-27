using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Notion;

/// <summary>Reads a <see cref="NotionProperty"/> by its <c>type</c> field, regardless of field order.</summary>
public sealed class NotionPropertyConverter : JsonConverter<NotionProperty>
{
    public override NotionProperty Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

        return type switch
        {
            "url" => new NotionUrlProperty { Id = id, Url = GetNullableString(root, "url") },
            "date" => new NotionDateProperty { Id = id },
            "files" => new NotionFilesProperty { Id = id },
            "number" => new NotionNumberProperty { Id = id },
            "relation" => new NotionRelationProperty { Id = id },
            "select" => new NotionSelectProperty { Id = id },
            "multi_select" => new NotionMultiSelectProperty { Id = id },
            "status" => new NotionStatusProperty { Id = id },
            "rich_text" => new NotionRichTextProperty { Id = id },
            "title" => new NotionTitleProperty { Id = id },
            _ => new NotionUnknownProperty { Id = id },
        };
    }

    public override void Write(Utf8JsonWriter writer, NotionProperty value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Fetchshelf never writes Notion properties back out as JSON.");

    private static string? GetNullableString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
