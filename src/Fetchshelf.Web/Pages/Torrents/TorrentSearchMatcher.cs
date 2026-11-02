using System.Globalization;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>
/// The Torrents list's search: every word of the query must appear in the title, an author or a series name,
/// ignoring case and accents, so "bronte jane" finds "Jane Eyre" by Charlotte Brontë.
/// </summary>
public static class TorrentSearchMatcher
{
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static bool Matches(TorrentMetadata metadata, string query)
    {
        var fields = new List<string> { metadata.Title };
        fields.AddRange(metadata.Authors);
        fields.AddRange(metadata.Series.Select(series => series.Name));

        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => fields.Any(field => CultureInfo.InvariantCulture.CompareInfo.IndexOf(field, word, Loose) >= 0));
    }
}
