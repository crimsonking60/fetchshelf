using System.Globalization;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>Extracts an edition phrase from title or tags text.</summary>
public static partial class EditionExtractor
{
    // The descriptor before "Edition"/"ed." is bounded to known ordinal shapes (optionally followed
    // by one qualifier word) or a bare qualifier word, rather than an open-ended word run - otherwise
    // a greedy match can swallow unrelated preceding title words (e.g. "UK Edition" inside
    // "Some Book UK Edition" must not capture "Some Book UK" as the descriptor).
    private const string OrdinalToken = @"\d{1,4}(?:st|nd|rd|th)?|First|Second|Third|Fourth|Fifth|Sixth|Seventh|Eighth|Ninth|Tenth";
    private const string QualifierWord = "Anniversary|Canadian|American|British|UK|US|International|Deluxe|Special|Revised|Expanded|Illustrated|Director's|Limited|Complete|Collector's";

    [GeneratedRegex(
        $@"(?<open>[\(\[]|-\s*)?(?<descriptor>(?:{OrdinalToken})(?:\s+(?:{QualifierWord}))?|(?:{QualifierWord}))\s+(?<label>Edition|ed\.)\s*(?<close>[\)\]])?(?<colon>:)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex EditionPhrasePattern();

    private static readonly Dictionary<string, int> OrdinalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = 1,
        ["second"] = 2,
        ["third"] = 3,
        ["fourth"] = 4,
        ["fifth"] = 5,
        ["sixth"] = 6,
        ["seventh"] = 7,
        ["eighth"] = 8,
        ["ninth"] = 9,
        ["tenth"] = 10,
    };

    /// <summary>A matched edition phrase: the formatted label, its ordinal number, and the matched span.</summary>
    public sealed record EditionMatch(string Label, int Number, int Index, int Length);

    /// <summary>Finds the first edition phrase in <paramref name="text"/>, or null if none is found.</summary>
    public static EditionMatch? TryExtract(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = EditionPhrasePattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var descriptor = match.Groups["descriptor"].Value;
        return new EditionMatch(FormatLabel(descriptor), ExtractNumber(descriptor), match.Index, match.Length);
    }

    /// <summary>
    /// Removes the matched edition phrase from <paramref name="title"/>. Any text that followed the
    /// phrase becomes a ": subtitle" suffix on the remaining text.
    /// </summary>
    public static string RemoveFromTitle(string title, EditionMatch match)
    {
        var before = title[..match.Index].TrimEnd();
        var after = title[(match.Index + match.Length)..].Trim();
        return after.Length > 0 ? $"{before}: {after}" : before;
    }

    private static int ExtractNumber(string descriptor)
    {
        var firstToken = descriptor.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var digits = new string(firstToken.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length > 0 && digits.Length <= 3)
        {
            return int.Parse(digits, CultureInfo.InvariantCulture);
        }

        return OrdinalWords.TryGetValue(firstToken, out var value) ? value : 0;
    }

    private static string FormatLabel(string descriptor)
    {
        var tokens = descriptor
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCaseToken)
            .ToList();
        tokens.Add("Edition");
        return string.Join(' ', tokens);
    }

    private static string TitleCaseToken(string token)
    {
        if (token.Length == 0 || char.IsDigit(token[0]))
        {
            return token;
        }

        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }
}
