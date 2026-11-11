namespace Fetchshelf.Core.Metadata;

/// <summary>Whether a person row is one of a torrent's authors or one of its narrators.</summary>
public enum PersonRole
{
    Author = 0,
    Narrator = 1,
}

/// <summary>One author or narrator of a torrent, in the order the metadata lists them.</summary>
public sealed record ProjectedPerson(PersonRole Role, int Position, string Name, string NameKey);

/// <summary>
/// One entry of one of a torrent's series. A series without a number has a single row with no
/// <see cref="Kind"/> and no numbers, so "every book in this series" still finds it.
/// </summary>
public sealed record ProjectedSeriesEntry(
    int Position, string Name, string NameKey, SeriesEntryKind? Kind, decimal? FirstNumber, decimal? LastNumber, string? Display);

/// <summary>
/// What of a torrent's metadata is stored as rows and columns next to the metadata JSON, so authors, series, language
/// and media type can be queried without reading every torrent's JSON.
/// </summary>
public sealed record TorrentProjection(
    IReadOnlyList<ProjectedPerson> People, IReadOnlyList<ProjectedSeriesEntry> Series, string Language, string MediaType)
{
    /// <summary>
    /// Authors and narrators each keep their order, and a name listed twice in one role (by its
    /// <see cref="PersonNameKey"/>) is kept once. Series names are keyed by <see cref="SeriesNameKey"/>; a series whose
    /// name keys as nothing is left out, and an entry listed twice is kept once.
    /// </summary>
    public static TorrentProjection From(TorrentMetadata metadata)
    {
        var people = new List<ProjectedPerson>();
        AddPeople(people, PersonRole.Author, metadata.Authors);
        AddPeople(people, PersonRole.Narrator, metadata.Narrators);

        var series = new List<ProjectedSeriesEntry>();
        var seen = new HashSet<(string Key, string? Display)>();
        foreach (var one in metadata.Series)
        {
            var name = one.Name.Trim();
            var key = SeriesNameKey.Of(name);
            if (key.Length == 0)
            {
                continue;
            }

            if (one.Entries.Count == 0)
            {
                if (seen.Add((key, null)))
                {
                    series.Add(new ProjectedSeriesEntry(series.Count, name, key, null, null, null, null));
                }

                continue;
            }

            foreach (var entry in one.SortedEntries)
            {
                if (seen.Add((key, entry.Display)))
                {
                    series.Add(new ProjectedSeriesEntry(
                        series.Count, name, key, entry.Kind, entry.FirstNumber, entry.LastNumber, entry.Display));
                }
            }
        }

        return new TorrentProjection(people, series, metadata.Language.ToString(), metadata.MediaType.ToString());
    }

    private static void AddPeople(List<ProjectedPerson> people, PersonRole role, IReadOnlyList<string> names)
    {
        var position = 0;
        var keys = new HashSet<string>();
        foreach (var raw in names)
        {
            var name = raw.Trim();
            var key = PersonNameKey.Of(name);
            if (key.Length > 0 && keys.Add(key))
            {
                people.Add(new ProjectedPerson(role, position++, name, key));
            }
        }
    }
}

/// <summary>
/// The key series names are compared by: the title key (<see cref="TitleNormalizer"/>), so case, punctuation, accents
/// and a leading article don't split one series in two. A blank name keys as an empty string.
/// </summary>
public static class SeriesNameKey
{
    public static string Of(string name) => string.IsNullOrWhiteSpace(name) ? string.Empty : TitleNormalizer.Normalize(name);
}
