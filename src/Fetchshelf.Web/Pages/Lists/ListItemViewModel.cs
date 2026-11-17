namespace Fetchshelf.Web.Pages.Lists;

/// <summary>What was found for one format of a book, as shown on the list page.</summary>
public sealed record ListItemSideViewModel(string Text, string? Link);

/// <summary>One book on a list page.</summary>
public sealed record ListItemViewModel
{
    public required string ExternalId { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }

    public string? SeriesText { get; init; }

    public string? CoverUrl { get; init; }

    public string? BookUrl { get; init; }

    public required string MamSearchUrl { get; init; }

    /// <summary>For a bookmark, the torrent's details on the Search page, shown instead of a MAM search.</summary>
    public string? TorrentDetailsUrl { get; init; }

    /// <summary>Null when the list's grab blocks never look for audiobooks.</summary>
    public ListItemSideViewModel? Audio { get; init; }

    /// <summary>Null when the list's grab blocks never look for ebooks.</summary>
    public ListItemSideViewModel? Ebook { get; init; }

    public bool MarkedDone { get; init; }

    public bool Removed { get; init; }

    /// <summary>Whether **Search again** is offered: the book was searched before, and a format is still missing.</summary>
    public bool CanSearchAgain { get; init; }

    /// <summary>When the list's runs next search MAM for the book, if a format is still missing and it was searched before.</summary>
    public DateTimeOffset? NextSearch { get; init; }

    /// <summary>Since when MAM has had nothing for the book; null when the last search found something.</summary>
    public DateTimeOffset? MissingSince { get; init; }
}
