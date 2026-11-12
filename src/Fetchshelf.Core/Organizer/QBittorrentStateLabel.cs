namespace Fetchshelf.Core.Organizer;

/// <summary>
/// Readable labels for qBittorrent's raw torrent state strings. Values it does not know (including
/// Fetchshelf's own "not in client" status) are shown unchanged. A label is a stage of the
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

    /// <summary>
    /// The states of a torrent that has finished downloading: seeding, queued or stopped after it finished, or checked
    /// as a finished one. For a database query.
    /// </summary>
    public static readonly IReadOnlyList<string> FinishedStages =
        ["uploading", "stalledUP", "forcedUP", "queuedUP", "pausedUP", "stoppedUP", "checkingUP"];

    /// <summary>
    /// Whether <paramref name="state"/> is a stage of a torrent that has finished downloading. The others are not all
    /// downloading: a torrent just added is checked first (checkingResumeData), one can be moving, missing files or in
    /// error, and none of those says it has finished.
    /// </summary>
    public static bool IsFinishedStage(string? state) => state is not null && FinishedStages.Contains(state);

    /// <summary>Whether two states are the same stage, i.e. show the same label.</summary>
    public static bool SameStage(string? a, string? b) => For(a) == For(b);
}
