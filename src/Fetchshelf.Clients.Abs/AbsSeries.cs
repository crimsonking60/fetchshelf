using System.Text.Json.Serialization;

namespace Fetchshelf.Clients.Abs;

/// <summary>`GET /api/libraries/{id}/series` response: one page of a library's series.</summary>
public sealed record AbsSeriesPage
{
    [JsonPropertyName("results")]
    public required IReadOnlyList<AbsSeriesResult> Results { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

/// <summary>One series of a library, with its books as library items.</summary>
public sealed record AbsSeriesResult
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("books")]
    public IReadOnlyList<AbsSeriesBookItem> Books { get; init; } = [];
}

/// <summary>A book of a series, as the library item Audiobookshelf returns for it.</summary>
public sealed record AbsSeriesBookItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("media")]
    public AbsBookMedia? Media { get; init; }
}

/// <summary>A book item's media, minified: its formats and its details.</summary>
public sealed record AbsBookMedia
{
    [JsonPropertyName("numTracks")]
    public int NumTracks { get; init; }

    [JsonPropertyName("ebookFormat")]
    public string? EbookFormat { get; init; }

    [JsonPropertyName("metadata")]
    public AbsBookMetadata? Metadata { get; init; }
}

/// <summary>A book's details, as far as the series list needs them.</summary>
public sealed record AbsBookMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Every series the book is in, with its place: <c>"Saga #1"</c>, <c>"Saga #1, Other #2"</c>, or a name alone.</summary>
    [JsonPropertyName("seriesName")]
    public string? SeriesName { get; init; }

    /// <summary>The language as entered in Audiobookshelf, such as "English"; free text, often empty.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }
}

/// <summary>A series of an Audiobookshelf library, with the books it has.</summary>
public sealed record AbsSeriesInfo(string Name, IReadOnlyList<AbsSeriesBook> Books);

/// <summary>
/// One book of a series in Audiobookshelf: its place in the series as written (<c>"3"</c>, <c>"1.5"</c>,
/// <c>"2163-2212"</c>), null when it has none, which formats Audiobookshelf has of it, and its language as entered there.
/// </summary>
public sealed record AbsSeriesBook(string ItemId, string Title, string? Sequence, bool HasAudio, bool HasEbook, string? Language = null);

/// <summary>Reads a book's place in one series out of Audiobookshelf's <c>seriesName</c>.</summary>
public static class AbsSeriesSequence
{
    /// <summary>
    /// The sequence after <paramref name="seriesName"/> in <paramref name="joined"/>: <c>"2"</c> for <c>"Blandings Castle"</c>
    /// in <c>"Blandings Castle #2, Psmith #4"</c>; null when the book has no place in it, or the series isn't named. The
    /// series is found by its whole name, as a name can itself hold <c>", "</c> (<c>"As You Wake, Break the Shell #1"</c>).
    /// </summary>
    public static string? Of(string? joined, string seriesName)
    {
        if (string.IsNullOrEmpty(joined) || string.IsNullOrEmpty(seriesName))
        {
            return null;
        }

        for (var at = joined.IndexOf(seriesName, StringComparison.Ordinal); at >= 0; at = joined.IndexOf(seriesName, at + 1, StringComparison.Ordinal))
        {
            var startsEntry = at == 0 || (at >= 2 && joined[(at - 2)..at] == ", ");
            var rest = joined[(at + seriesName.Length)..];
            if (!startsEntry || !rest.StartsWith(" #", StringComparison.Ordinal))
            {
                continue;
            }

            var sequence = rest[2..];
            var end = sequence.IndexOf(", ", StringComparison.Ordinal);
            sequence = (end < 0 ? sequence : sequence[..end]).Trim();
            return sequence.Length == 0 ? null : sequence;
        }

        return null;
    }
}
