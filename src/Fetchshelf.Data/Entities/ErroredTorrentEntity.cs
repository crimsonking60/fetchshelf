namespace Fetchshelf.Data.Entities;

/// <summary>Pipeline stages that can record an error against a torrent, Fetchshelf-spec.md §3.2.</summary>
public enum ErrorStage
{
    Grabber,
    Linker,
    Cleaner,
}

/// <summary>One row per (stage, torrent) error; a successful run of the stage clears it (Fetchshelf-spec.md §3.2).</summary>
public sealed class ErroredTorrentEntity
{
    public int Id { get; set; }

    public required ErrorStage Stage { get; set; }

    /// <summary>MaM id (Grabber stage) or info-hash (Linker/Cleaner stage), as a string.</summary>
    public required string Key { get; set; }

    public required string ErrorText { get; set; }

    public string? DisplayMetadataJson { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }
}
