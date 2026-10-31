namespace Fetchshelf.Core.Metadata;

/// <summary>A series: a name plus its entries.</summary>
public sealed record Series
{
    public required string Name { get; init; }

    public required IReadOnlyList<SeriesEntry> Entries { get; init; }

    /// <summary>True if the entry text could not be parsed and entries was produced empty: a warning, not a failure.</summary>
    public bool HadParseWarning { get; init; }

    /// <summary>Entries sorted by first number; ties ordered single &lt; part &lt; range.</summary>
    public IReadOnlyList<SeriesEntry> SortedEntries =>
        Entries.OrderBy(e => e.FirstNumber).ThenBy(e => e.Kind).ToList();
}
