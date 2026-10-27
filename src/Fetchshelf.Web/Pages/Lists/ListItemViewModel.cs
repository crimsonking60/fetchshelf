namespace Fetchshelf.Web.Pages.Lists;

/// <summary>One book on the List detail page (Fetchshelf-spec.md §10.4, §7.8).</summary>
public sealed record ListItemViewModel
{
    public required string Guid { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }

    public string? SeriesText { get; init; }

    public required string CoverUrl { get; init; }

    public string? BookUrl { get; init; }

    public required string MamSearchUrl { get; init; }

    public bool AudioAllowed { get; init; }

    public string? AudioDisplay { get; init; }

    public string? AudioLink { get; init; }

    public bool AudioMissing { get; init; }

    public bool EbookAllowed { get; init; }

    public string? EbookDisplay { get; init; }

    public string? EbookLink { get; init; }

    public bool EbookMissing { get; init; }

    public bool CanMarkDone { get; init; }
}
