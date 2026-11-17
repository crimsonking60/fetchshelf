namespace Fetchshelf.Core.Lists;

/// <summary>A book on a list of wanted books, whatever the list's source: what the list importer searches MAM for.</summary>
/// <param name="ExternalId">
/// The book's id at its source, the same from one read to the next: the list keeps one item per id.
/// </param>
/// <param name="Title">The title, without a series in it.</param>
/// <param name="Authors">The writers, first author first; may be empty.</param>
public sealed record ListBook(string ExternalId, string Title, IReadOnlyList<string> Authors)
{
    public string? SeriesName { get; init; }

    /// <summary>The book's place in <see cref="SeriesName"/>; null for an unnumbered entry or when there is no series.</summary>
    public double? SeriesNumber { get; init; }

    public string? CoverUrl { get; init; }

    /// <summary>The book's page at its source, if it has one.</summary>
    public string? BookUrl { get; init; }

    /// <summary>ISBNs and ASINs as the source writes them; an upload carrying one of them is the book.</summary>
    public IReadOnlyList<string> Identifiers { get; init; } = [];
}
