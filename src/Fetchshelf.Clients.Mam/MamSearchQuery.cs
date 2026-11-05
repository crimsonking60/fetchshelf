namespace Fetchshelf.Clients.Mam;

/// <summary>A MAM torrent search request. Unset fields impose no filter.</summary>
public sealed record MamSearchQuery
{
    public string? Text { get; init; }

    public IReadOnlyList<MamSearchField> SearchFields { get; init; } = [];

    public MamSearchTarget? Target { get; init; }

    public MamSearchKind? Kind { get; init; }

    public IReadOnlyList<int> LanguageIds { get; init; } = [];

    public IReadOnlyList<int> MainCategoryIds { get; init; } = [];

    public IReadOnlyList<int> CategoryIds { get; init; } = [];

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public long? MinSizeBytes { get; init; }

    public long? MaxSizeBytes { get; init; }

    public long? MinSeeders { get; init; }

    public long? MaxSeeders { get; init; }

    public long? MinLeechers { get; init; }

    public long? MaxLeechers { get; init; }

    public long? MinSnatched { get; init; }

    public long? MaxSnatched { get; init; }

    /// <summary>When set, <see cref="Flags"/> are shown if true or hidden if false; unset imposes no flag filter.</summary>
    public bool? ShowFlags { get; init; }

    public IReadOnlyList<int> Flags { get; init; } = [];

    public MamSortOrder SortOrder { get; init; } = MamSortOrder.NewestFirst;

    /// <summary>Number of results to skip, for paging.</summary>
    public long PageOffset { get; init; }

    /// <summary>5 to 100 results per page.</summary>
    public long PerPage { get; init; } = 100;

    public bool ReturnDownloadLink { get; init; }

    /// <summary>Also return each torrent's cover image (<see cref="MamTorrentRecord.Thumbnail"/>).</summary>
    public bool ReturnThumbnail { get; init; }

    /// <summary>Also return each torrent's technical file details (<see cref="MamTorrentRecord.MediaInfoJson"/>).</summary>
    public bool ReturnMediaInfo { get; init; }

    /// <summary>Set to look up one torrent by MAM id instead of searching.</summary>
    public long? Id { get; init; }

    /// <summary>Set to look up one torrent by info-hash instead of searching.</summary>
    public string? Hash { get; init; }
}
