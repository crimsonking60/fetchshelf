using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>The matching-relevant fields of a configured `[[library]]` entry.</summary>
public sealed record LibraryMatchRule
{
    public string? Category { get; init; }

    public string? DownloadDir { get; init; }

    public IReadOnlyList<string> AllowTags { get; init; } = [];

    public IReadOnlyList<string> DenyTags { get; init; } = [];

    /// <summary>What the library asks of a book's genres and main category; <see cref="GenreCondition.None"/> takes every book.</summary>
    public GenreCondition Genres { get; init; } = GenreCondition.None;
}

/// <summary>Matches a finished torrent to a configured library.</summary>
public static class LibraryMatcher
{
    /// <summary>
    /// The first library that takes the torrent by its qBittorrent category, save path and tags, whatever its genre
    /// condition; null when none does.
    /// </summary>
    public static int? FindMatchingLibraryIndex(
        IReadOnlyList<LibraryMatchRule> libraries,
        string? qbittorrentCategory,
        string savePath,
        IReadOnlyList<string> torrentTags) =>
        CandidateIndexes(libraries, qbittorrentCategory, savePath, torrentTags) is [var first, ..] ? first : null;

    /// <summary>
    /// Every library that could take the torrent, in the order of the configuration: its category or download folder
    /// matches, and its tags pass. Needs no metadata, so a torrent with no candidate costs no MAM request.
    /// </summary>
    public static IReadOnlyList<int> CandidateIndexes(
        IReadOnlyList<LibraryMatchRule> libraries,
        string? qbittorrentCategory,
        string savePath,
        IReadOnlyList<string> torrentTags)
    {
        var candidates = new List<int>();
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
                candidates.Add(i);
            }
        }

        return candidates;
    }

    /// <summary>The first of <paramref name="candidates"/> whose genre condition takes the book; null when none does.</summary>
    public static int? ChooseForBook(IReadOnlyList<LibraryMatchRule> libraries, IReadOnlyList<int> candidates, TorrentMetadata metadata)
    {
        foreach (var index in candidates)
        {
            if (libraries[index].Genres.Matches(metadata))
            {
                return index;
            }
        }

        return null;
    }
}
