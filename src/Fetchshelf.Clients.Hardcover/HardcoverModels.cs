namespace Fetchshelf.Clients.Hardcover;

/// <summary>The Hardcover user a token belongs to.</summary>
public sealed record HardcoverUser(long Id, string Username);

/// <summary>A book's place in one of its series; the position is null for unnumbered entries.</summary>
public sealed record HardcoverSeriesEntry(string Name, double? Position);

/// <summary>A book on one of the user's shelves or lists, with what Fetchshelf needs to find it on MAM.</summary>
public sealed record HardcoverBook
{
    public required long Id { get; init; }

    public required string Title { get; init; }

    /// <summary>The book's page is https://hardcover.app/books/{Slug}.</summary>
    public string? Slug { get; init; }

    /// <summary>The writers only: narrators, illustrators, translators and the like are left out.</summary>
    public required IReadOnlyList<string> Authors { get; init; }

    public required IReadOnlyList<HardcoverSeriesEntry> Series { get; init; }

    public string? CoverUrl { get; init; }

    public string? Url => Slug is null ? null : $"https://hardcover.app/books/{Uri.EscapeDataString(Slug)}";
}

/// <summary>Hardcover's reading statuses, with the ids its API uses.</summary>
public enum HardcoverReadingStatus
{
    WantToRead = 1,
    CurrentlyReading = 2,
    Read = 3,
    Paused = 4,
    DidNotFinish = 5,
}
