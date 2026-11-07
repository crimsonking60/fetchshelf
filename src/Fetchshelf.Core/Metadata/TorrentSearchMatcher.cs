using System.Globalization;

namespace Fetchshelf.Core.Metadata;

/// <summary>
/// The Torrents list's search: every word of the query must appear in the title, an author or a series name,
/// ignoring case and accents, so "bronte jane" finds "Jane Eyre" by Charlotte Brontë.
/// </summary>
public static class TorrentSearchMatcher
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static bool Matches(TorrentMetadata metadata, string query) =>
        MatchesAny([metadata.Title, .. metadata.Authors, .. metadata.Series.Select(series => series.Name)], query);

    /// <summary>Whether every word of <paramref name="query"/> appears in one of <paramref name="fields"/>, ignoring case and accents.</summary>
    public static bool MatchesAny(IReadOnlyList<string> fields, string query) =>
        query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => fields.Any(field => CultureInfo.InvariantCulture.CompareInfo.IndexOf(field, word, Loose) >= 0));
}
