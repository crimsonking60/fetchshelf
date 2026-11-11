namespace Fetchshelf.Data.Entities;

/// <summary>The jobs that record an error per item they couldn't handle. Stored as a number, so new values go at the end.</summary>
public enum ErrorStage
{
    Grabber,
    Linker,
    Cleaner,

    /// <summary>A snatch list row the snatchlist importer couldn't import; keyed by MAM id.</summary>
    Snatchlist,

    /// <summary>A book a Hardcover list couldn't search for; keyed by <see cref="ErrorLog.ListItemKey"/>.</summary>
    HardcoverList,

    /// <summary>A linked torrent the Audiobookshelf matcher couldn't match; keyed by info-hash.</summary>
    Audiobookshelf,

    /// <summary>A followed series or author the Follow job couldn't search for; keyed by <see cref="ErrorLog.FollowKey"/>.</summary>
    Follow,
}

/// <summary>One row per (stage, torrent) error; a successful run of the stage clears it.</summary>
public sealed class ErroredTorrentEntity
{
    public int Id { get; set; }

    public required ErrorStage Stage { get; set; }

    /// <summary>The item the error is about: a MAM id (Grabber, Snatchlist), an info-hash (Linker, Cleaner, Audiobookshelf), or a list item (HardcoverList).</summary>
    public required string Key { get; set; }

    /// <summary>The torrent's name when the error was recorded, so the row is recognizable without a Torrent record.</summary>
    public string? Title { get; set; }

    public required string ErrorText { get; set; }

    public string? DisplayMetadataJson { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }
}
