using System.Globalization;
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

    [GeneratedRegex(@"[^a-z0-9_ ]")]
    private static partial Regex InvalidCharacterPattern();

    [GeneratedRegex(@"\b(volume|vol)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeWordPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesPattern();

    /// <summary>
    /// Normalizes <paramref name="title"/>: transliterate to ASCII, lowercase, replace
    /// " &amp; " with " and ", remove a leading "the "/"a "/"an ", remove all characters that are not
    /// letters, digits, underscore or space, then remove the words "volume" and "vol".
    /// </summary>
    public static string Normalize(string title)
    {
        var ascii = TransliterateToAscii(title);
        var lower = ascii.ToLowerInvariant();
        var withAnd = lower.Replace(" & ", " and ");
        var withoutLeadingArticle = LeadingArticlePattern().Replace(withAnd, "", 1);
        var validCharsOnly = InvalidCharacterPattern().Replace(withoutLeadingArticle, "");
        var withoutVolumeWords = VolumeWordPattern().Replace(validCharsOnly, "");
        var collapsed = MultipleSpacesPattern().Replace(withoutVolumeWords, " ");
        return collapsed.Trim();
    }

    private static string TransliterateToAscii(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // strip combining diacritics (e.g. decomposed "e" + "́" -> "e")
            }

            if (c < 128)
            {
                builder.Append(c);
            }

            // else: non-ASCII character with no decomposed ASCII base (e.g. non-Latin script) - drop it,
            // since the normalized key must be ASCII.
        }

        return builder.ToString();
    }
}
