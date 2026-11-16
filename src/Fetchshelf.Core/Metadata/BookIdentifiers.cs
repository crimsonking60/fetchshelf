using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// ISBNs and ASINs as keys two sources can be compared by. MAM's uploads and Hardcover's editions write them every which
/// way: with hyphens or spaces, as ISBN-10 or ISBN-13, an ASIN with or without <c>ASIN:</c> in front, and a print book's
/// ASIN is its ISBN-10. Each value becomes every key it stands for, so an ISBN-10 on one side matches the ISBN-13 on the
/// other. A value whose check digit is wrong, or that is no ISBN or ASIN at all, gives no key: a mistyped number must not
/// match some other book.
/// </summary>
public static partial class BookIdentifiers
{
    /// <summary>The keys of every value given, upper-cased; values that give none are left out.</summary>
    public static IReadOnlySet<string> From(IEnumerable<string?> values) =>
        values.SelectMany(Keys).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The keys one value stands for: an ISBN-13 and, for a 978 one, its ISBN-10; an ISBN-10 and its ISBN-13; or an ASIN
    /// (<c>B</c> and nine letters or digits).
    /// </summary>
    public static IEnumerable<string> Keys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var compact = Separators().Replace(value, string.Empty).ToUpperInvariant();
        if (compact.StartsWith("ASIN", StringComparison.Ordinal))
        {
            compact = compact[4..].TrimStart(':');
        }

        if (IsIsbn13(compact))
        {
            yield return compact;
            if (compact.StartsWith("978", StringComparison.Ordinal))
            {
                yield return Isbn10Of(compact[3..12]);
            }
        }
        else if (IsIsbn10(compact))
        {
            yield return compact;
            yield return Isbn13Of("978" + compact[..9]);
        }
        else if (Asin().IsMatch(compact))
        {
            yield return compact;
        }
    }

    private static bool IsIsbn13(string value)
    {
        if (value.Length != 13 || !value.All(char.IsAsciiDigit) || !(value.StartsWith("978", StringComparison.Ordinal) || value.StartsWith("979", StringComparison.Ordinal)))
        {
            return false;
        }

        return Isbn13Of(value[..12]) == value;
    }

    private static bool IsIsbn10(string value) =>
        value.Length == 10 && value[..9].All(char.IsAsciiDigit) && (char.IsAsciiDigit(value[9]) || value[9] == 'X')
        && Isbn10Of(value[..9]) == value;

    /// <summary>Twelve digits with their ISBN-13 check digit.</summary>
    private static string Isbn13Of(string first12)
    {
        var sum = first12.Select((digit, i) => (digit - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return first12 + (char)('0' + ((10 - (sum % 10)) % 10));
    }

    /// <summary>Nine digits with their ISBN-10 check digit, <c>X</c> for ten.</summary>
    private static string Isbn10Of(string first9)
    {
        var sum = first9.Select((digit, i) => (digit - '0') * (10 - i)).Sum();
        var check = (11 - (sum % 11)) % 11;
        return first9 + (check == 10 ? 'X' : (char)('0' + check));
    }

    [GeneratedRegex(@"[\s\-]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^B[0-9A-Z]{9}$")]
    private static partial Regex Asin();
}
