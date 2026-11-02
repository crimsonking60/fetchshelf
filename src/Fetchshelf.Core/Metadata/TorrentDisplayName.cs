namespace Fetchshelf.Core.Metadata;

/// <summary>How a torrent is named in log messages: its title and, when it has any, its authors.</summary>
public static class TorrentDisplayName
{
    private const int MaxAuthorsNamed = 2;

    /// <summary>"The Way of Kings by Brandon Sanderson"; past two authors, "by A, B and 3 others".</summary>
    public static string DisplayName(this TorrentMetadata metadata)
    {
        var authors = metadata.Authors;
        if (authors.Count == 0)
        {
            return metadata.Title;
        }

        var named = string.Join(", ", authors.Take(MaxAuthorsNamed));
        var others = authors.Count - MaxAuthorsNamed;
        return others > 0
            ? $"{metadata.Title} by {named} and {others} {(others == 1 ? "other" : "others")}"
            : $"{metadata.Title} by {named}";
    }
}
