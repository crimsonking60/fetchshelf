namespace Fetchshelf.Clients.Mam;

/// <summary>A page of MAM search results.</summary>
public sealed record MamSearchResult
{
    public required int PerPage { get; init; }

    public required int Start { get; init; }

    public required int Total { get; init; }

    public required int Found { get; init; }

    public required IReadOnlyList<MamTorrentRecord> Data { get; init; }
}

/// <summary>One raw MAM torrent record, as documented (before cleaning).</summary>
public sealed record MamTorrentRecord
{
    public required long MamId { get; init; }

    public required string Title { get; init; }

    public required IReadOnlyDictionary<long, string> Authors { get; init; }

    public required IReadOnlyDictionary<long, string> Narrators { get; init; }

    public required IReadOnlyList<MamSeriesInfo> Series { get; init; }

    /// <summary>Plain ISBN, or an ASIN prefixed with "ASIN:".</summary>
    public string? Isbn { get; init; }

    public string? Description { get; init; }

    public required int MediaTypeId { get; init; }

    /// <summary>The old main-category fallback used when <see cref="MediaTypeId"/> is absent/zero.</summary>
    public required int OldMainCategoryId { get; init; }

    public required int FictionCategoryId { get; init; }

    public required int LegacyCategoryId { get; init; }

    public required IReadOnlyList<int> GenreCategoryIds { get; init; }

    public required int LanguageId { get; init; }

    public required int FlagsRaw { get; init; }

    /// <summary>Space-separated file extensions.</summary>
    public required string FileTypesRaw { get; init; }

    public required int FileCount { get; init; }

    /// <summary>Raw size string, e.g. "1.23 GiB".</summary>
    public required string SizeRaw { get; init; }

    public required string TagsRaw { get; init; }

    public required long UploaderId { get; init; }

    public required string UploaderName { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }

    public required long Seeders { get; init; }

    public required long Leechers { get; init; }

    public required long TimesCompleted { get; init; }

    public required bool IsFree { get; init; }

    public required bool IsPersonalFreeleech { get; init; }

    public required bool IsFreeleechVip { get; init; }

    public required bool IsVip { get; init; }

    /// <summary>Unix timestamp; 0 means permanent VIP.</summary>
    public required long VipExpireUnixSeconds { get; init; }

    /// <summary>The hash used to build a download URL.</summary>
    public string? DownloadHash { get; init; }

    /// <summary>The cover image's address, when the search asked for it (<see cref="MamSearchQuery.ReturnThumbnail"/>).</summary>
    public string? Thumbnail { get; init; }

    /// <summary>
    /// The files' technical details as a JSON object (sections such as "General" and "Audio1"), when the search asked
    /// for them (<see cref="MamSearchQuery.ReturnMediaInfo"/>) and MAM has them. MAM sends it as JSON inside a string.
    /// </summary>
    public string? MediaInfoJson { get; init; }

    /// <summary>Every field MAM returned for the torrent, as the JSON object it sent.</summary>
    public string RawJson { get; init; } = "{}";
}

public sealed record MamSeriesInfo(string Name, string Number);
