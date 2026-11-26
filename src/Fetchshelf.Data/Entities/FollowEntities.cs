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
    /// <summary>What is found is selected, through the usual selection rules and budgets. Shown as *Auto*.</summary>
    Download = 0,

    /// <summary>What is found is listed on the Following page, with a Download button; nothing is selected. Shown as *Follow*.</summary>
    Watch = 1,

    /// <summary>Series only: left out of the series with missing books, and never searched.</summary>
    Ignore = 2,
}

/// <summary>Where a follow came from.</summary>
public enum FollowSource
{
    /// <summary>Followed from a page of Fetchshelf's.</summary>
    Manual = 0,

    /// <summary>Added by the Follow job for a series in Audiobookshelf (`[follow] audiobookshelf_series`).</summary>
    Audiobookshelf = 1,
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

    public FollowSource Source { get; set; } = FollowSource.Manual;

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the Follow job last searched MAM for it; null until then, or when a search was asked for by hand.</summary>
    public DateTimeOffset? LastSearchedAt { get; set; }

    /// <summary>When a search last found something new for it.</summary>
    public DateTimeOffset? LastFoundAt { get; set; }

    /// <summary>
    /// When a search last read every page MAM had for it, not only those uploaded since the search before; null until
    /// the first such search.
    /// </summary>
    public DateTimeOffset? FullySearchedAt { get; set; }
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
    /// <summary>
    /// The torrent's language (<c>Language</c> enum name): a book in two languages is two finds. Null for a find recorded
    /// before languages were kept, until a search finds it again.
    /// </summary>
    public string? Language { get; set; }

    public required string MediaSide { get; set; }

    /// <summary>
    /// The torrent's media type (<c>MediaType</c> enum name), such as "ComicBook", as shown in the Format column; null for
    /// a find recorded before media types were kept, until a search finds it again. <see cref="MediaSide"/> is what the
    /// Follow job matches by.
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>The torrent's size as MAM gave it when last found; null for finds recorded before sizes were kept.</summary>
    public long? SizeBytes { get; set; }

    public required FollowFindStatus Status { get; set; }

    public required DateTimeOffset FoundAt { get; set; }

    public required DateTimeOffset StatusAt { get; set; }
}

/// <summary>
/// One book of a series in Audiobookshelf, in one format, as the Follow job last read it there
/// (`[follow] audiobookshelf_series`). The library counts it as owned, so a book Audiobookshelf has from before Fetchshelf
/// isn't searched for or downloaded again. Books Fetchshelf linked itself are left out: their torrents count already.
/// Every sync replaces the rows.
/// </summary>
public sealed class AudiobookshelfSeriesBookEntity
{
    public long Id { get; set; }

    /// <summary>The Audiobookshelf library item.</summary>
    public required string ItemId { get; set; }

    public required string Name { get; set; }

    /// <summary><c>SeriesNameKey</c> of the name, as the series rows of torrents have it.</summary>
    public required string NameKey { get; set; }

    public required string Title { get; set; }

    /// <summary>The book's place as shown: "3", "1-3"; null when it has none.</summary>
    public string? Display { get; set; }

    public double? FirstNumber { get; set; }

    public double? LastNumber { get; set; }

    /// <summary>"Audiobook" or "Ebook", as a torrent's media type is stored: the format Audiobookshelf has.</summary>
    public required string MediaType { get; set; }

    /// <summary>
    /// The book's language in Audiobookshelf (<c>Language</c> enum name); null when it has none Fetchshelf knows, and then
    /// it counts as owned in every language.
    /// </summary>
    public string? Language { get; set; }

    public required DateTimeOffset SyncedAt { get; set; }
}

/// <summary>
/// A series someone stopped following, so `[follow] audiobookshelf_series` doesn't follow it again on the next run.
/// Following the series again removes the row.
/// </summary>
public sealed class StoppedSeriesEntity
{
    /// <summary><c>SeriesNameKey</c> of the series.</summary>
    public required string NameKey { get; set; }

    public required DateTimeOffset StoppedAt { get; set; }
}
