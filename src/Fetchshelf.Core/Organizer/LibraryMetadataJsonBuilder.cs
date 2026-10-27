using System.Text.Json;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Builds the Audiobookshelf-compatible `metadata.json` document, Fetchshelf-spec.md §8.7.</summary>
public static class LibraryMetadataJsonBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build(TorrentMetadata metadata)
    {
        var (title, subtitle) = SplitTitle(metadata.Title);

        var document = new MetadataJsonDocument
        {
            Authors = metadata.Authors,
            Narrators = metadata.Narrators,
            Series = metadata.Series.Select(FormatSeriesDisplay).ToList(),
            Title = title,
            Subtitle = subtitle,
            Description = metadata.Description,
            Isbn = metadata.Isbn,
            Asin = metadata.Asin,
            Tags = metadata.Flags.HasFlag(BookFlags.Lgbt) ? ["LGBT"] : [],
            Genres = BuildGenres(metadata),
            Language = metadata.Language.ToString(),
            Explicit = metadata.Flags.HasFlag(BookFlags.Explicit),
            Abridged = metadata.Flags.HasFlag(BookFlags.Abridged),
        };

        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>Legacy category plus genre categories, de-duplicated (Fetchshelf-spec.md §8.7) - shared with
    /// the Audiobookshelf client's metadata push, which sends the same derived genre list.</summary>
    public static IReadOnlyList<string> BuildGenres(TorrentMetadata metadata)
    {
        var genres = new List<string> { LegacyCategoryDisplay.DisplayName(metadata.LegacyCategory) };
        genres.AddRange(metadata.GenreCategories.Select(GenreCategoryDisplay.DisplayName));
        return genres.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The series' entry numbers formatted as a display string (e.g. "1, 2"), or null when
    /// the series has no entries - shared with the Audiobookshelf client's `sequence` field.</summary>
    public static string? FormatSequence(Series series) =>
        series.Entries.Count > 0 ? string.Join(", ", series.SortedEntries.Select(e => e.Display)) : null;

    private static string FormatSeriesDisplay(Series series) =>
        series.Entries.Count > 0 ? $"{series.Name} #{FormatSequence(series)}" : series.Name;

    /// <summary>Splits a title at its first colon into title/subtitle, unless the part before the
    /// colon is shorter than 4 characters (Fetchshelf-spec.md §8.7) - shared with the Audiobookshelf client's
    /// metadata push, which applies the same split.</summary>
    public static (string Title, string? Subtitle) SplitTitle(string title)
    {
        var colonIndex = title.IndexOf(':');
        if (colonIndex < 0)
        {
            return (title, null);
        }

        var beforeColon = title[..colonIndex].Trim();
        if (beforeColon.Length < 4)
        {
            return (title, null);
        }

        var subtitle = title[(colonIndex + 1)..].Trim();
        return (beforeColon, subtitle.Length > 0 ? subtitle : null);
    }
}

/// <summary>The `metadata.json` document shape, Fetchshelf-spec.md §8.7.</summary>
public sealed record MetadataJsonDocument
{
    public required IReadOnlyList<string> Authors { get; init; }

    public required IReadOnlyList<string> Narrators { get; init; }

    public required IReadOnlyList<string> Series { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string? Description { get; init; }

    public string? Isbn { get; init; }

    public string? Asin { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public required IReadOnlyList<string> Genres { get; init; }

    public required string Language { get; init; }

    public required bool Explicit { get; init; }

    public required bool Abridged { get; init; }
}

internal static class LegacyCategoryDisplay
{
    private static readonly string[] Prefixes = ["Audiobook", "Ebook", "Musicology", "Radio"];

    public static string DisplayName(LegacyCategory category)
    {
        var name = category.ToString();
        foreach (var prefix in Prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        return SplitPascalCase(name);
    }

    private static string SplitPascalCase(string value) =>
        string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
}
