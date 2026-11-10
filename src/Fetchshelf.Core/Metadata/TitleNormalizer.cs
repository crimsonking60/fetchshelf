using System.Text;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Derives the normalized title key ("title_search") used as the "same book" lookup key.
/// </summary>
public static partial class TitleNormalizer
{
    [GeneratedRegex(@"^(the|a|an)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingArticlePattern();

    [GeneratedRegex(@"\b(volume|vol)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeWordPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesPattern();

    /// <summary>
    /// Normalizes <paramref name="title"/>: fold it (<see cref="TextFolding"/>: Latin accents off, other scripts kept),
    /// lowercase, replace " &amp; " with " and ", remove a leading "the "/"a "/"an ", remove all characters that are
    /// not letters, digits, underscore or space, then remove the words "volume" and "vol". A title with nothing left
    /// after that, such as one made only of symbols, keys as itself, trimmed and lowercased, so only the identical
    /// title matches it. An empty or blank title keys as an empty string, which callers treat as matching nothing.
    /// </summary>
    public static string Normalize(string title)
    {
        var folded = TextFolding.Fold(title);
        var lower = folded.ToLowerInvariant();
        var withAnd = lower.Replace(" & ", " and ");
        var withoutLeadingArticle = LeadingArticlePattern().Replace(withAnd, "", 1);
        var validCharsOnly = LettersDigitsUnderscoresAndSpaces(withoutLeadingArticle);
        var withoutVolumeWords = VolumeWordPattern().Replace(validCharsOnly, "");
        var collapsed = MultipleSpacesPattern().Replace(withoutVolumeWords, " ").Trim();
        return collapsed.Length > 0 ? collapsed : title.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Keeps letters and digits of any script, underscores and spaces. Works on whole characters rather than UTF-16
    /// units, so a letter outside the Basic Multilingual Plane (a rare ideograph) isn't split and dropped.
    /// </summary>
    private static string LettersDigitsUnderscoresAndSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || Rune.IsDigit(rune) || rune.Value is '_' or ' ')
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }
}
