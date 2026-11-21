namespace Fetchshelf.Core.Catalog;

/// <summary>
/// Compares file paths ignoring case, with runs of digits compared as numbers, so "Part 2" comes before "Part 10" and
/// "Disc 2/01.mp3" before "Disc 10/01.mp3". Doesn't depend on the culture.
/// </summary>
public sealed class NumberAwareComparer : IComparer<string>
{
    public static readonly NumberAwareComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var (xStart, yStart) = (i, j);
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var xDigits = x.AsSpan(xStart, i - xStart).TrimStart('0');
                var yDigits = y.AsSpan(yStart, j - yStart).TrimStart('0');
                var byValue = xDigits.Length != yDigits.Length
                    ? xDigits.Length.CompareTo(yDigits.Length)
                    : xDigits.SequenceCompareTo(yDigits);
                if (byValue != 0)
                {
                    return byValue;
                }

                continue;
            }

            var byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        var byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }
}
