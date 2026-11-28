using System.Globalization;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using FuzzySharp;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>Pairs a search candidate's metadata with an opaque caller payload (e.g. the raw search record) for ranking.</summary>
public sealed record ListCandidate<T>
{
    public required TorrentMetadata Metadata { get; init; }

    public required T Payload { get; init; }
}

/// <summary>
/// Ranks MAM search results against a list item. First by identifier: results whose ISBN or ASIN is one of the book's
/// (<see cref="IdentifierMatches"/>) are the book for certain. Otherwise by a fuzzy score for the title, authors and
/// series, keeping those within a fixed margin of the best (<see cref="RankAndFilter"/>). Either way, the user's file
/// types come first, then fewer files, then larger size.
/// </summary>
public static class ListCandidateRanker
{
    private const int ScoreMargin = 100;

    /// <summary>
    /// The candidates whose ISBN or ASIN is among <paramref name="identifiers"/> (keys from
    /// <see cref="BookIdentifiers"/>), best format first. Empty when the book has no identifiers or none matches.
    /// </summary>
    public static IReadOnlyList<ListCandidate<T>> IdentifierMatches<T>(
        IReadOnlyList<ListCandidate<T>> candidates, IReadOnlySet<string> identifiers, IReadOnlyList<string> preferredFileTypes) =>
        IdentifierMatches(candidates, identifiers, _ => preferredFileTypes);

    /// <inheritdoc cref="IdentifierMatches{T}(IReadOnlyList{ListCandidate{T}}, IReadOnlySet{string}, IReadOnlyList{string})"/>
    /// <param name="preferredFileTypesFor">The format list for a candidate's media type, as comics can have their own.</param>
    public static IReadOnlyList<ListCandidate<T>> IdentifierMatches<T>(
        IReadOnlyList<ListCandidate<T>> candidates, IReadOnlySet<string> identifiers, Func<MediaType, IReadOnlyList<string>> preferredFileTypesFor) =>
        identifiers.Count == 0
            ? []
            : OrderByFormat(
                candidates.Where(c => BookIdentifiers.From([c.Metadata.Isbn, c.Metadata.Asin]).Overlaps(identifiers)),
                preferredFileTypesFor);

    /// <summary>
    /// Returns candidates within <see cref="ScoreMargin"/> of the best fuzzy score, ordered best
    /// first (preferred file type, then fewer files, then larger size breaking ties). Empty input
    /// yields empty output.
    /// </summary>
    public static IReadOnlyList<ListCandidate<T>> RankAndFilter<T>(
        IReadOnlyList<ListCandidate<T>> candidates,
        string title,
        IReadOnlyList<string> authors,
        (string Name, double Number)? series,
        IReadOnlyList<string> preferredFileTypes) =>
        RankAndFilter(candidates, title, authors, series, _ => preferredFileTypes);

    /// <inheritdoc cref="RankAndFilter{T}(IReadOnlyList{ListCandidate{T}}, string, IReadOnlyList{string}, ValueTuple{string, double}?, IReadOnlyList{string})"/>
    /// <param name="preferredFileTypesFor">The format list for a candidate's media type, as comics can have their own.</param>
    public static IReadOnlyList<ListCandidate<T>> RankAndFilter<T>(
        IReadOnlyList<ListCandidate<T>> candidates,
        string title,
        IReadOnlyList<string> authors,
        (string Name, double Number)? series,
        Func<MediaType, IReadOnlyList<string>> preferredFileTypesFor)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var scored = candidates
            .Select(c => (Candidate: c, Score: Score(c.Metadata, title, authors, series)))
            .OrderByDescending(s => s.Score)
            .ToList();

        var maxScore = scored[0].Score;
        var withinMargin = scored.Where(s => s.Score > maxScore - ScoreMargin);

        return OrderByFormat(withinMargin.Select(s => s.Candidate), preferredFileTypesFor);
    }

    private static List<ListCandidate<T>> OrderByFormat<T>(
        IEnumerable<ListCandidate<T>> candidates, Func<MediaType, IReadOnlyList<string>> preferredFileTypesFor) =>
        candidates
            .Select(c => (Candidate: c, Rank: FormatRanker.Rank(preferredFileTypesFor(c.Metadata.MediaType), c.Metadata.FileTypes)))
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.Candidate.Metadata.FileCount)
            .ThenByDescending(s => s.Candidate.Metadata.TotalSizeBytes)
            .Select(s => s.Candidate)
            .ToList();

    private static int Score(TorrentMetadata metadata, string title, IReadOnlyList<string> authors, (string Name, double Number)? series)
    {
        var titleScore = Fuzz.Ratio(title, metadata.Title);

        var authorScore = metadata.Authors.Count == 0 || authors.Count == 0
            ? 0
            : metadata.Authors.Max(candidateAuthor => authors.Max(author => AuthorNameKey.Similarity(author, candidateAuthor)));

        var seriesScore = series is { } s ? SeriesScore(s, metadata.Series) : 0;

        return (titleScore * 2) + (authorScore * 2) + seriesScore;
    }

    private static int SeriesScore((string Name, double Number) series, IReadOnlyList<Series> candidateSeries)
    {
        if (candidateSeries.Count == 0)
        {
            return 0;
        }

        var numberText = series.Number.ToString(CultureInfo.InvariantCulture);
        return candidateSeries.Max(candidate =>
        {
            var nameScore = Fuzz.Ratio(series.Name, candidate.Name);
            var numberScore = candidate.Entries.Count == 0
                ? 0
                : candidate.Entries.Max(entry => Fuzz.Ratio(numberText, entry.Display));
            return nameScore + numberScore;
        });
    }
}
