using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Pages.Search;

/// <summary>A labelled list of name/value pairs, such as one section of a torrent's media info.</summary>
public sealed record DetailSection(string Title, IReadOnlyList<(string Name, string Value)> Rows);

/// <summary>Turns the raw parts of a MAM search hit into what its page shows.</summary>
public static partial class MamHitDetails
{
    /// <summary>
    /// The description as plain text: MAM sends HTML written by the uploader, which must not reach the page as markup.
    /// Line breaks and paragraphs become new lines, scripts and styles go with their content, other tags are dropped
    /// and entities decoded.
    /// </summary>
    public static string? DescriptionText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = ScriptOrStylePattern().Replace(html, string.Empty);
        text = LineBreakPattern().Replace(text, "\n");
        text = TagPattern().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = BlankLinesPattern().Replace(text.Replace("\r\n", "\n", StringComparison.Ordinal), "\n\n");
        return text.Trim() is { Length: > 0 } trimmed ? trimmed : null;
    }

    /// <summary>The cover's address, made absolute against MAM when MAM sends a path; null for anything else.</summary>
    public static string? ThumbnailUrl(string? thumbnail, string mamBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnail))
        {
            return null;
        }

        // A path first: on Linux "/covers/a.jpg" would also pass as an absolute file:// address.
        if (thumbnail.StartsWith('/') && !thumbnail.StartsWith("//", StringComparison.Ordinal))
        {
            return $"{mamBaseUrl.TrimEnd('/')}{thumbnail}";
        }

        return Uri.TryCreate(thumbnail, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http"
            ? absolute.ToString()
            : null;
    }

    /// <summary>
    /// Where MAM keeps a torrent's cover: <c>{cdn}/t/p/{number}/large/{MAM id}.{type}</c>, with the type from
    /// <c>poster_type</c>. MAM's own pages put the cover's upload time in the number, but its CDN doesn't look at it;
    /// the torrent's upload time is used, so a changed cover still gets a new address. Null when the torrent has no
    /// cover, or one of a type this doesn't know.
    /// </summary>
    public static string? CoverUrl(long mamId, string? posterType, DateTimeOffset uploadedAt, string cdnBaseUrl)
    {
        var extension = posterType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpeg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => null,
        };
        return extension is null
            ? null
            : $"{cdnBaseUrl.TrimEnd('/')}/t/p/{uploadedAt.ToUnixTimeSeconds()}/large/{mamId}.{extension}";
    }

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

    [GeneratedRegex(@"<\s*br\s*/?\s*>|<\s*/\s*p\s*>|<\s*/\s*div\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakPattern();

    [GeneratedRegex(@"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStylePattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\n[ \t]*\n(\s*\n)+")]
    private static partial Regex BlankLinesPattern();
}
