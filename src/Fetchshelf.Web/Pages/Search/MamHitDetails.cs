using System.Text.Json;

namespace Fetchshelf.Web.Pages.Search;

/// <summary>A labelled list of name/value pairs, such as one section of a torrent's media info.</summary>
public sealed record DetailSection(string Title, IReadOnlyList<(string Name, string Value)> Rows);

/// <summary>Turns the raw parts of a MAM search hit into what its page shows.</summary>
public static class MamHitDetails
{
    /// <summary>One section per part of the media info ("General", "Audio1", ...); values that aren't sections go in a first, untitled one.</summary>
    public static IReadOnlyList<DetailSection> MediaInfoSections(string? mediaInfoJson)
    {
        if (mediaInfoJson is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(mediaInfoJson);
        var loose = new List<(string, string)>();
        var sections = new List<DetailSection>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                sections.Add(new DetailSection(property.Name, property.Value.EnumerateObject().Select(p => (p.Name, ValueText(p.Value))).ToList()));
            }
            else
            {
                loose.Add((property.Name, ValueText(property.Value)));
            }
        }

        return loose.Count == 0 ? sections : [new DetailSection(string.Empty, loose), .. sections];
    }

    /// <summary>Every field MAM sent, in its order, with the value as text.</summary>
    public static IReadOnlyList<(string Name, string Value)> RawFields(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.EnumerateObject().Select(p => (p.Name, ValueText(p.Value))).ToList()
            : [];
    }

    private static string ValueText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Array when value.EnumerateArray().All(e => e.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) =>
            string.Join(", ", value.EnumerateArray().Select(ValueText)),
        _ => value.GetRawText(),
    };
}
