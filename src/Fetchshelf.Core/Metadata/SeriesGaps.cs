using System.Globalization;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// Which books of a series are owned and which are missing, from the owned entries' numbers. Only whole book numbers
/// are reasoned about: a range covers every number in it, a part (<c>2p1</c>) counts as its book, and a fractional
/// entry (<c>1.5</c>, a novella between books) is owned when present but never reported missing.
/// </summary>
public static class SeriesGaps
{
    /// <summary>
    /// The whole numbers from 1 to the highest owned whole number that no owned entry covers: owned <c>1, 2, 3-4, 6</c>
    /// gives <c>5</c>; owned <c>3</c> gives <c>1, 2</c>. Nothing when no whole number is owned.
    /// </summary>
    /// <param name="owned">Each owned entry's first and last number; entries without a number are left out.</param>
    public static IReadOnlyList<int> Missing(IEnumerable<(double First, double Last)> owned)
    {
        var covered = Covered(owned);
        if (covered.Count == 0)
        {
            return [];
        }

        return Enumerable.Range(1, covered.Max()).Where(n => !covered.Contains(n)).ToList();
    }

    /// <summary>The whole book numbers the entries cover.</summary>
    public static HashSet<int> Covered(IEnumerable<(double First, double Last)> owned)
    {
        var covered = new HashSet<int>();
        foreach (var (first, last) in owned)
        {
            // A fractional first number covers no whole book; a range "1.5-3" still covers 2 and 3.
            var from = (int)Math.Ceiling(first);
            var to = (int)Math.Floor(Math.Max(first, last));
            for (var n = Math.Max(from, 1); n <= to && n <= MaxBookNumber; n++)
            {
                covered.Add(n);
            }
        }

        return covered;
    }

    /// <summary>
    /// Whole numbers written short, runs of three or more as ranges: <c>1, 2, 3, 4, 6</c> as "1–4, 6", <c>1, 2</c> as
    /// "1, 2".
    /// </summary>
    public static string Describe(IEnumerable<int> numbers)
    {
        var sorted = numbers.Distinct().Order().ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
            {
                j++;
            }

            if (j - i >= 2)
            {
                parts.Add($"{Format(sorted[i])}–{Format(sorted[j])}");
            }
            else
            {
                parts.AddRange(sorted.Skip(i).Take(j - i + 1).Select(Format));
            }

            i = j + 1;
        }

        return string.Join(", ", parts);
    }

    /// <summary>A series numbered beyond this is not a run of books to fill in, such as one numbered by year.</summary>
    public const int MaxBookNumber = 500;

    private static string Format(int number) => number.ToString(CultureInfo.InvariantCulture);
}
