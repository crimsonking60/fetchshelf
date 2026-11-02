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

    /// <summary>The torrent's metadata as the row carries it; null when the row has only the basic fields above.</summary>
    public MamSnatchlistDetails? Details { get; init; }
}

/// <summary>
/// The metadata a snatch list row carries. Compared with a search record it lacks the media type, language,
/// file count, upload date, fiction category, description and ISBN.
/// </summary>
public sealed record MamSnatchlistDetails
{
    public required int LegacyCategoryId { get; init; }

    public required IReadOnlyList<int> GenreCategoryIds { get; init; }

    public required IReadOnlyDictionary<long, string> Authors { get; init; }

    public required IReadOnlyDictionary<long, string> Narrators { get; init; }

    /// <summary>In series id order.</summary>
    public required IReadOnlyList<MamSeriesInfo> Series { get; init; }

    public required IReadOnlyList<string> FileTypes { get; init; }

    public required string SizeRaw { get; init; }

    public required string TagsRaw { get; init; }

    public required int FlagsRaw { get; init; }

    public required bool IsFree { get; init; }

    public required bool IsPersonalFreeleech { get; init; }

    public required bool IsVip { get; init; }

    public required long VipExpireUnixSeconds { get; init; }

    public required long UploaderId { get; init; }

    public required string UploaderName { get; init; }

    public required long TimesCompleted { get; init; }
}
