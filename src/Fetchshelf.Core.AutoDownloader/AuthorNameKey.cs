using System.Text.RegularExpressions;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// An author's name as list matching compares it: lower case, with punctuation as spaces, and without a suffix such as
/// "Jr." at the end. Lists and MAM write the same name differently, "A.E. van Vogt" and "A E van Vogt", or
/// "Henry Dowd Jr." and "Henry Dowd", and the full stops alone kept such names below the match threshold.
/// </summary>
public static partial class AuthorNameKey
{
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotLetterOrDigitPattern();

    [GeneratedRegex(@" (?:jr|jnr|sr|snr|ii|iii|iv)$")]
    private static partial Regex SuffixPattern();

    public static string Of(string name)
    {
        var words = NotLetterOrDigitPattern().Replace(name.ToLowerInvariant(), " ").Trim();
        return SuffixPattern().Replace(words, string.Empty) is { Length: > 0 } withoutSuffix ? withoutSuffix : words;
    }

    /// <summary>How alike two authors' names are, 0 to 100, compared by <see cref="Of"/>.</summary>
    public static int Similarity(string first, string second) => FuzzySharp.Fuzz.Ratio(Of(first), Of(second));
}
