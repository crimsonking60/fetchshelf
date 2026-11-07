namespace Fetchshelf.Core.Organizer;

/// <summary>
/// Readable labels for qBittorrent's raw torrent state strings. Values it does not know (including
/// Fetchshelf's own "not in client" / "removed from MAM" statuses) are shown unchanged. A label is a stage of the
/// torrent's life: states that share one, such as a seeding torrent's uploading and stalledUP, are the same stage.
/// </summary>
public static class QBittorrentStateLabel
{
    public static string? For(string? state) => state switch
    {
        "uploading" or "stalledUP" or "forcedUP" => "Seeding",
        "queuedUP" => "Queued to seed",
        "pausedUP" or "stoppedUP" => "Stopped",
        "downloading" or "forcedDL" => "Downloading",
        "stalledDL" => "Downloading (stalled)",
        "queuedDL" => "Queued to download",
        "pausedDL" or "stoppedDL" => "Stopped (incomplete)",
        "metaDL" or "forcedMetaDL" => "Fetching metadata",
        "checkingUP" or "checkingDL" or "checkingResumeData" => "Checking",
        "allocating" => "Allocating",
        "moving" => "Moving",
        "missingFiles" => "Missing files",
        "error" => "Error",
        "unknown" => "Unknown",
        _ => state,
    };

    /// <summary>Whether <paramref name="state"/> is a stage of a torrent that hasn't finished downloading.</summary>
    public static bool IsDownloadStage(string? state) => state is
        "downloading" or "stalledDL" or "queuedDL" or "forcedDL" or "metaDL" or "forcedMetaDL"
        or "pausedDL" or "stoppedDL" or "checkingDL" or "allocating";

    /// <summary>Whether two states are the same stage, i.e. show the same label.</summary>
    public static bool SameStage(string? a, string? b) => For(a) == For(b);
}
