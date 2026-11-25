namespace Fetchshelf.Core.Organizer;

/// <summary>How <see cref="CollectionSplitter"/> read a collection's books.</summary>
public enum SplitStrategy
{
    /// <summary>MAM's series has a range, such as <c>1-40</c>: every book is in that series.</summary>
    SeriesRange,

    /// <summary>A magazine's issues (plan <c>magazine-runs.md</c>): the magazine is the series.</summary>
    MagazineRun,

    /// <summary>Books named <c>&lt;series&gt; &lt;number&gt;</c>, of one series or several.</summary>
    NumberedSeries,

    /// <summary>A reading order across series, each name starting with the book's place in it.</summary>
    ReadingOrder,

    /// <summary>Unrelated books by several of the torrent's authors.</summary>
    Bundle,
}

/// <summary>How a magazine run's issues are numbered.</summary>
public enum MagazineNumbering
{
    /// <summary>Not numbered: no numbering gave every issue a number of its own.</summary>
    None,

    /// <summary>By the issue number: <c>Issue 512</c> is 512.</summary>
    IssueNumber,

    /// <summary>By the year and month: <c>1990-01</c> is 1990.01.</summary>
    YearAndMonth,

    /// <summary>By the volume and number: <c>v10n04</c> is 10.04.</summary>
    VolumeAndNumber,
}

/// <summary>What a split had to guess at.</summary>
public enum SplitWarningKind
{
    /// <summary>A magazine run's issues have no numbering that gives each a number of its own.</summary>
    NumberingNotRecognized,

    /// <summary>The books use different numberings, or some don't read as issues at all.</summary>
    InconsistentNumbering,

    /// <summary>The issues use several magazine names, and MAM lists no series to choose by.</summary>
    SeveralMagazineNames,

    /// <summary>Some books are left without a number while most have one.</summary>
    BooksWithoutNumber,
}

/// <summary>A warning about a split, with a few of the books' names it concerns.</summary>
public sealed record SplitWarning(SplitWarningKind Kind, string Message, IReadOnlyList<string> Examples);

/// <summary>
/// What <see cref="CollectionSplitter.Analyze"/> made of a torrent: its books and how they were read, or why it is left
/// as one book, and what it had to guess at.
/// </summary>
/// <param name="Books">The books; null when the torrent isn't split.</param>
/// <param name="Strategy">How the books were read; null when the torrent isn't split.</param>
/// <param name="Numbering">A magazine run's numbering; <see cref="MagazineNumbering.None"/> otherwise.</param>
/// <param name="NotSplitReason">
/// Why a torrent of several books is left as one; null when it is split, or when there is nothing to split (one book,
/// or not on the ebook side).
/// </param>
public sealed record CollectionSplit(
    IReadOnlyList<CollectionBook>? Books,
    SplitStrategy? Strategy,
    MagazineNumbering Numbering,
    string? NotSplitReason,
    IReadOnlyList<SplitWarning> Warnings)
{
    /// <summary>Nothing to split, and nothing to say about it.</summary>
    public static CollectionSplit Nothing { get; } = new(null, null, MagazineNumbering.None, null, []);

    /// <summary>How the books were read, in words, such as "split as a magazine run, numbered by issue".</summary>
    public string? Description => Strategy switch
    {
        SplitStrategy.SeriesRange => "split by MAM's series range",
        SplitStrategy.MagazineRun => "split as a magazine run, " + Numbering switch
        {
            MagazineNumbering.IssueNumber => "numbered by issue",
            MagazineNumbering.YearAndMonth => "numbered by year and month",
            MagazineNumbering.VolumeAndNumber => "numbered by volume and number",
            _ => "not numbered",
        },
        SplitStrategy.NumberedSeries => "split as a series numbered in its file names",
        SplitStrategy.ReadingOrder => "split as a reading order",
        SplitStrategy.Bundle => "split as a bundle of books by several authors",
        _ => null,
    };
}
