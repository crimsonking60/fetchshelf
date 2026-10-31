namespace Fetchshelf.Clients.Mam;

/// <summary>One row of a snatch list page.</summary>
public sealed record MamSnatchlistRow
{
    public required long MamId { get; init; }

    public required string Title { get; init; }

    public required long Seeders { get; init; }

    public required long Leechers { get; init; }

    public required bool Finished { get; init; }

    public string? DownloadHash { get; init; }
}
