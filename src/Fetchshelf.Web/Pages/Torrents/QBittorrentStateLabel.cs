namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>
/// Readable labels for qBittorrent's raw torrent state strings. Values it does not know (including
/// Fetchshelf's own "not in client" / "removed from MaM" statuses) are shown unchanged.
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
}
