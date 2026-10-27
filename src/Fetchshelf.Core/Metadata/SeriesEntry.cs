namespace Fetchshelf.Core.Metadata;

/// <summary>
/// The kind of a series entry, Fetchshelf-spec.md §6.3. Declared in sort-tie-break order: when two entries
/// share the same <see cref="SeriesEntry.FirstNumber"/>, a single number sorts before a part, which
/// sorts before a range.
/// </summary>
public enum SeriesEntryKind
{
    Number = 0,
    Part = 1,
    Range = 2,
}

/// <summary>
/// One entry in a series: a number ("3", "1.5", ".5"), a range ("1-3"), or a part ("2p1" = book 2
/// part 1). Fetchshelf-spec.md §6.3.
/// </summary>
public sealed record SeriesEntry
{
    public required SeriesEntryKind Kind { get; init; }

    /// <summary>The first number in the entry, used for sort order.</summary>
    public required decimal FirstNumber { get; init; }

    /// <summary>The formatted display form: the number as written, "a-b" for a range, "NpM" for a part.</summary>
    public required string Display { get; init; }

    public static SeriesEntry ForNumber(decimal value, string display) =>
        new() { Kind = SeriesEntryKind.Number, FirstNumber = value, Display = display };

    public static SeriesEntry ForRange(decimal first, decimal second) =>
        new() { Kind = SeriesEntryKind.Range, FirstNumber = first, Display = $"{FormatNumber(first)}-{FormatNumber(second)}" };

    public static SeriesEntry ForPart(decimal bookNumber, decimal partNumber) =>
        new() { Kind = SeriesEntryKind.Part, FirstNumber = bookNumber, Display = $"{FormatNumber(bookNumber)}p{FormatNumber(partNumber)}" };

    internal static string FormatNumber(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
