using Fetchshelf.Core.Metadata;
using FuzzySharp;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// Fuzzy-matches a list item's title and authors against the library, so a book already owned in a format is not
/// searched for again in that format.
/// </summary>
public static class ListLibraryMatcher
{
    private const int TitleScoreThreshold = 80;
    private const int AuthorScoreThreshold = 90;

    /// <summary>
    /// Returns the first candidate of <paramref name="mainType"/> whose title scores at least
    /// <see cref="TitleScoreThreshold"/> and shares at least one author scoring above
    /// <see cref="AuthorScoreThreshold"/> with <paramref name="authors"/>, or null if none match.
    /// </summary>
    public static ListCandidate<T>? FindMatch<T>(
        string title, IReadOnlyList<string> authors, FilterMainType mainType, IEnumerable<ListCandidate<T>> candidates)
    {
        foreach (var candidate in candidates)
        {
            var metadata = candidate.Metadata;
            if (FilterMainTypeMapper.FromMediaType(metadata.MediaType) != mainType)
            {
                continue;
            }

            if (Fuzz.Ratio(title, metadata.Title) < TitleScoreThreshold)
            {
                continue;
            }

            var hasMatchingAuthor = metadata.Authors.Any(
                candidateAuthor => authors.Any(author => Fuzz.Ratio(author, candidateAuthor) > AuthorScoreThreshold));
            if (!hasMatchingAuthor)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }
}
