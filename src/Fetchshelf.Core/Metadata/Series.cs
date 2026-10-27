namespace Fetchshelf.Core.Metadata;

/// <summary>A series: a name plus its entries. Fetchshelf-spec.md §3.1, §6.3.</summary>
public sealed record Series
{
    public required string Name { get; init; }

    public required IReadOnlyList<SeriesEntry> Entries { get; init; }

    /// <summary>True if the entry text could not be parsed and entries was produced empty (§6.3: "warn, don't fail").</summary>
    public bool HadParseWarning { get; init; }

    /// <summary>Entries sorted by first number; ties ordered single &lt; part &lt; range (§6.3).</summary>
    public IReadOnlyList<SeriesEntry> SortedEntries =>
        Entries.OrderBy(e => e.FirstNumber).ThenBy(e => e.Kind).ToList();
}
