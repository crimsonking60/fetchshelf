namespace Fetchshelf.Data.Entities;

/// <summary>Pipeline stages that can record an error against a torrent.</summary>
public enum ErrorStage
{
    Grabber,
    Linker,
    Cleaner,
}

/// <summary>One row per (stage, torrent) error; a successful run of the stage clears it.</summary>
public sealed class ErroredTorrentEntity
{
    public int Id { get; set; }

    public required ErrorStage Stage { get; set; }

    /// <summary>MAM id (Grabber stage) or info-hash (Linker/Cleaner stage), as a string.</summary>
    public required string Key { get; set; }

    /// <summary>The torrent's name when the error was recorded, so the row is recognizable without a Torrent record.</summary>
    public string? Title { get; set; }

    public required string ErrorText { get; set; }

    public string? DisplayMetadataJson { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }
}
