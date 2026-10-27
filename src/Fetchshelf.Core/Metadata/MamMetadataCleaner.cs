using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>Cleaning rules applied to every record built from MaM data, Fetchshelf-spec.md §6.4.</summary>
public static partial class MamMetadataCleaner
{
    [GeneratedRegex(@"\(Digital\)\s*$|\(Light Novel\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesSuffixPattern();

    [GeneratedRegex(@":\s*A\s+(Novel|Memoir)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleNoisePattern();

    [GeneratedRegex(@"\(digital\)|\[epub\]|\(unabridged\)|\(light novel\)", RegexOptions.IgnoreCase)]
    private static partial Regex FormatMarkerNoisePattern();

    [GeneratedRegex(@"-\s*\d+\.epub\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EpubSuffixNoisePattern();

    /// <summary>Decodes HTML entities in a name or series string (§6.4).</summary>
    public static string DecodeHtmlEntities(string text) => WebUtility.HtmlDecode(text);

    /// <summary>
    /// Normalizes casing for an author/narrator name: for each space-separated word longer than 3
    /// characters, an all-caps word is lower-cased after its first letter, and an all-lowercase word
    /// gets its first letter capitalized (§6.4).
    /// </summary>
    public static string NormalizeNameCasing(string name)
    {
        var words = name.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = NormalizeWordCasing(words[i]);
        }

        return string.Join(' ', words);
    }

    private static string NormalizeWordCasing(string word)
    {
        if (word.Length <= 3)
        {
            return word;
        }

        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return word;
        }

        var isAllCaps = letters.All(char.IsUpper);
        var isAllLower = letters.All(char.IsLower);

        if (isAllCaps)
        {
            return word[0] + word[1..].ToLowerInvariant();
        }

        if (isAllLower)
        {
            return char.ToUpperInvariant(word[0]) + word[1..];
        }

        return word;
    }

    /// <summary>Strips a trailing "(Digital)" or "(Light Novel)" suffix from a series name (§6.4).</summary>
    public static string StripSeriesSuffix(string seriesName) =>
        SeriesSuffixPattern().Replace(seriesName, "").TrimEnd();

    /// <summary>
    /// Reclassifies an Ebook-typed record as Audiobook when its legacy category belongs to the
    /// audio category list (§6.4).
    /// </summary>
    public static MediaType ReclassifyMediaType(MediaType mediaType, LegacyCategory legacyCategory)
    {
        if (mediaType == MediaType.Ebook && LegacyCategoryGroups.IsAudiobookCategory(legacyCategory))
        {
            return MediaType.Audiobook;
        }

        return mediaType;
    }

    /// <summary>
    /// Strips a trailing " by &lt;Author&gt;" / " - &lt;Author&gt;" or leading "&lt;Author&gt; - "
    /// from the title, matching any of the record's authors, unless the record is a periodical with
    /// exactly one author (§6.4).
    /// </summary>
    public static string StripAuthorReference(string title, IReadOnlyList<string> authors, MediaType mediaType)
    {
        var isPeriodical = mediaType is MediaType.PeriodicalEbook or MediaType.PeriodicalAudiobook;
        if (isPeriodical && authors.Count == 1)
        {
            return title;
        }

        foreach (var author in authors)
        {
            var trailingBy = $" by {author}";
            if (title.EndsWith(trailingBy, StringComparison.Ordinal))
            {
                return title[..^trailingBy.Length];
            }

            var trailingDash = $" - {author}";
            if (title.EndsWith(trailingDash, StringComparison.Ordinal))
            {
                return title[..^trailingDash.Length];
            }

            var leadingDash = $"{author} - ";
            if (title.StartsWith(leadingDash, StringComparison.Ordinal))
            {
                return title[leadingDash.Length..];
            }
        }

        return title;
    }

    /// <summary>Removes known noise patterns from a title and trims the result (§6.4).</summary>
    public static string RemoveTitleNoise(string title)
    {
        var withoutSubtitleNoise = SubtitleNoisePattern().Replace(title, "");
        var withoutFormatMarkers = FormatMarkerNoisePattern().Replace(withoutSubtitleNoise, "");
        var withoutEpubSuffix = EpubSuffixNoisePattern().Replace(withoutFormatMarkers, "");
        return withoutEpubSuffix.Trim();
    }
}
