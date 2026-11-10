using System.Globalization;
using System.Text;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Folds text for comparison keys. ASCII stays as it is. A Latin letter with accents loses them ("é" → "e"), and the
/// Latin letters that have no accent-free form are spelled out ("ø" → "o", "ß" → "ss"), so "Søren" and "Soren" fold
/// alike. Letters and digits of every other script are kept exactly as written: taking their marks off would merge
/// letters that are different (Cyrillic "й" and "и", Japanese "が" and "か"). Anything else outside ASCII, such as
/// typographic punctuation, is dropped.
/// </summary>
public static class TextFolding
{
    private static readonly Dictionary<int, string> LatinWithoutDecomposition = new()
    {
        ['ø'] = "o",
        ['Ø'] = "O",
        ['æ'] = "ae",
        ['Æ'] = "AE",
        ['œ'] = "oe",
        ['Œ'] = "OE",
        ['ß'] = "ss",
        ['ẞ'] = "SS",
        ['ł'] = "l",
        ['Ł'] = "L",
        ['đ'] = "d",
        ['Đ'] = "D",
        ['ð'] = "d",
        ['Ð'] = "D",
        ['þ'] = "th",
        ['Þ'] = "TH",
        ['ı'] = "i",
    };

    public static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (rune.IsAscii)
            {
                builder.Append((char)rune.Value);
            }
            else if (FoldLatin(rune) is { } latin)
            {
                builder.Append(latin);
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }

    /// <summary>A Latin letter's accent-free spelling, or null for anything that isn't one.</summary>
    private static string? FoldLatin(Rune rune)
    {
        if (LatinWithoutDecomposition.TryGetValue(rune.Value, out var spelled))
        {
            return spelled;
        }

        var decomposed = rune.ToString().Normalize(NormalizationForm.FormD);
        var first = Rune.GetRuneAt(decomposed, 0);
        if (first.IsAscii && Rune.IsLetter(first) && decomposed.Length > first.Utf16SequenceLength
            && CharUnicodeInfo.GetUnicodeCategory(decomposed, first.Utf16SequenceLength) == UnicodeCategory.NonSpacingMark)
        {
            return first.ToString();
        }

        return LatinWithoutDecomposition.TryGetValue(first.Value, out var spelledBase) ? spelledBase : null;
    }
}
