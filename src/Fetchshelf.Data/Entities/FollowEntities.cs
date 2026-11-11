namespace Fetchshelf.Data.Entities;

/// <summary>What a follow is of.</summary>
public enum FollowKind
{
    Series = 0,
    Author = 1,
}

/// <summary>What the Follow job does for a followed series or author.</summary>
public enum FollowMode
{
    /// <summary>What is found is selected, through the usual selection rules and budgets.</summary>
    Download = 0,

    /// <summary>What is found is listed on the Following page, with a Download button; nothing is selected.</summary>
    Watch = 1,

    /// <summary>Series only: left out of the series with missing books, and never searched.</summary>
    Ignore = 2,
}

/// <summary>What became of a torrent a follow's search found.</summary>
public enum FollowFindStatus
{
    /// <summary>Found for a watched series or author, waiting to be looked at.</summary>
    New = 0,

    /// <summary>Selected for download, by the Follow job or from the Following page.</summary>
    Selected = 1,

    /// <summary>A match none of the `[[follow.grab]]` blocks' costs accepts, such as a torrent that isn't free.</summary>
    Wanted = 2,

    /// <summary>The library has the book by now.</summary>
    Owned = 3,

    /// <summary>Put aside on the Following page; never shown as new again.</summary>
    Dismissed = 4,
}

/// <summary>A series or author followed from the Series page, a torrent's page or the Torrents page's filters.</summary>
public sealed class FollowEntity
{
    public int Id { get; set; }

    public required FollowKind Kind { get; set; }

    /// <summary>The series' <c>SeriesNameKey</c> or the author's <c>PersonNameKey</c>, as in the series and people rows.</summary>
    public required string NameKey { get; set; }

    /// <summary>The name as shown, and as searched for on MAM.</summary>
    public required string DisplayName { get; set; }

    public required FollowMode Mode { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the Follow job last searched MAM for it; null until then, or when a search was asked for by hand.</summary>
    public DateTimeOffset? LastSearchedAt { get; set; }

    /// <summary>When a search last found something new for it.</summary>
    public DateTimeOffset? LastFoundAt { get; set; }
}

/// <summary>One torrent a follow's search found, and what became of it. One row per follow and MAM torrent.</summary>
public sealed class FollowFindEntity
{
    public int Id { get; set; }

    public required int FollowId { get; set; }

    public required long MamId { get; set; }

    public required string Title { get; set; }

    /// <summary>The entry in the followed series, such as "5"; null for an author's book or an unnumbered one.</summary>
    public string? SeriesDisplay { get; set; }

    /// <summary>"Audio" or "Ebook".</summary>
    public required string MediaSide { get; set; }

    public required FollowFindStatus Status { get; set; }

    public required DateTimeOffset FoundAt { get; set; }

    public required DateTimeOffset StatusAt { get; set; }
}
