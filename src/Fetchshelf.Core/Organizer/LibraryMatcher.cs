namespace Fetchshelf.Core.Organizer;

/// <summary>The matching-relevant fields of a configured `[[library]]` entry.</summary>
public sealed record LibraryMatchRule
{
    public string? Category { get; init; }

    public string? DownloadDir { get; init; }

    public IReadOnlyList<string> AllowTags { get; init; } = [];

    public IReadOnlyList<string> DenyTags { get; init; } = [];
}

/// <summary>Matches a finished torrent to a configured library.</summary>
public static class LibraryMatcher
{
    /// <summary>
    /// Returns the index, in <paramref name="libraries"/>, of the first library that matches, or null
    /// if none does. A library matches if its `category` equals the torrent's qBittorrent category (or
    /// the torrent's save path is in its `download_dir`, by whole folder names), it has no deny-tag present on the
    /// torrent, and - if it declares allow-tags - at least one is present on the torrent.
    /// </summary>
    public static int? FindMatchingLibraryIndex(
        IReadOnlyList<LibraryMatchRule> libraries,
        string? qbittorrentCategory,
        string savePath,
        IReadOnlyList<string> torrentTags)
    {
        for (var i = 0; i < libraries.Count; i++)
        {
            var library = libraries[i];

            if (library.DenyTags.Any(t => torrentTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (library.AllowTags.Count > 0
                && !library.AllowTags.Any(t => torrentTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var categoryMatches = library.Category is not null
                && string.Equals(library.Category, qbittorrentCategory, StringComparison.OrdinalIgnoreCase);
            var downloadDirMatches = library.DownloadDir is not null
                && PathPrefix.IsSameOrUnder(savePath, library.DownloadDir);

            if (categoryMatches || downloadDirMatches)
            {
                return i;
            }
        }

        return null;
    }
}
