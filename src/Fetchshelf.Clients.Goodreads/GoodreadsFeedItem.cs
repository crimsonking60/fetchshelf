namespace Fetchshelf.Clients.Goodreads;

/// <summary>One book from a Goodreads shelf RSS feed, Fetchshelf-spec.md §5.4, §7.8.</summary>
public sealed record GoodreadsFeedItem
{
    public required string Guid { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }

    public string? SeriesName { get; init; }

    public double? SeriesNumber { get; init; }

    public required string CoverUrl { get; init; }

    public string? BookUrl { get; init; }

    public string? Isbn { get; init; }
}
