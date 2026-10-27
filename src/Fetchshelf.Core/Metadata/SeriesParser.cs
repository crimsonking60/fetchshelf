using System.Globalization;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>Parses a series' comma-separated entry list, Fetchshelf-spec.md §6.3.</summary>
public static partial class SeriesParser
{
    [GeneratedRegex(@"^\s*(\d*\.\d+|\d+)\s*$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\s*(\d*\.\d+|\d+)\s*-\s*(\d*\.\d+|\d+)\s*$")]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"^\s*(\d*\.\d+|\d+)\s*p\s*(\d*\.\d+|\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PartPattern();

    /// <summary>
    /// Parses <paramref name="entriesText"/> into a <see cref="Series"/>. If any entry cannot be
    /// parsed as a number, range, or part, the result has no entries and
    /// <see cref="Series.HadParseWarning"/> set, rather than throwing (§6.3: "warn, don't fail").
    /// </summary>
    public static Series Parse(string name, string? entriesText)
    {
        if (string.IsNullOrWhiteSpace(entriesText))
        {
            return new Series { Name = name, Entries = [] };
        }

        var rawEntries = entriesText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var parsed = new List<SeriesEntry>(rawEntries.Length);
        foreach (var raw in rawEntries)
        {
            var entry = TryParseEntry(raw);
            if (entry is null)
            {
                return new Series { Name = name, Entries = [], HadParseWarning = true };
            }

            parsed.Add(entry);
        }

        return new Series { Name = name, Entries = parsed };
    }

    private static SeriesEntry? TryParseEntry(string raw)
    {
        var partMatch = PartPattern().Match(raw);
        if (partMatch.Success)
        {
            var book = ParseDecimal(partMatch.Groups[1].Value);
            var part = ParseDecimal(partMatch.Groups[2].Value);
            return SeriesEntry.ForPart(book, part);
        }

        var rangeMatch = RangePattern().Match(raw);
        if (rangeMatch.Success)
        {
            var first = ParseDecimal(rangeMatch.Groups[1].Value);
            var second = ParseDecimal(rangeMatch.Groups[2].Value);
            return SeriesEntry.ForRange(first, second);
        }

        var numberMatch = NumberPattern().Match(raw);
        if (numberMatch.Success)
        {
            var value = ParseDecimal(numberMatch.Groups[1].Value);
            return SeriesEntry.ForNumber(value, raw.Trim());
        }

        return null;
    }

    private static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
