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
/// Ranks MAM search results against a list item's title, authors and series: a fuzzy score for each, keeping
/// those within a fixed margin of the best, then preferring the user's file types, then fewer files, then larger
/// size.
/// </summary>
public static class ListCandidateRanker
{
    private const int ScoreMargin = 100;

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
        IReadOnlyList<string> preferredFileTypes)
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

        return withinMargin
            .Select(s => (s.Candidate, Rank: FormatRanker.Rank(preferredFileTypes, s.Candidate.Metadata.FileTypes)))
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.Candidate.Metadata.FileCount)
            .ThenByDescending(s => s.Candidate.Metadata.TotalSizeBytes)
            .Select(s => s.Candidate)
            .ToList();
    }

    private static int Score(TorrentMetadata metadata, string title, IReadOnlyList<string> authors, (string Name, double Number)? series)
    {
        var titleScore = Fuzz.Ratio(title, metadata.Title);

        var authorScore = metadata.Authors.Count == 0 || authors.Count == 0
            ? 0
            : metadata.Authors.Max(candidateAuthor => authors.Max(author => Fuzz.Ratio(author, candidateAuthor)));

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
