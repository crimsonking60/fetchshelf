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

    /// <summary>Null when the list's grab blocks never look for audiobooks.</summary>
    public ListItemSideViewModel? Audio { get; init; }

    /// <summary>Null when the list's grab blocks never look for ebooks.</summary>
    public ListItemSideViewModel? Ebook { get; init; }

    public bool MarkedDone { get; init; }

    public bool Removed { get; init; }
}
