using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Web.Pages.Torrents;

/// <summary>Where a tracked torrent stands with the library, as the Torrents page's Status filter sees it.</summary>
public enum TorrentLinkStatus
{
    Linked,
    Waiting,
    Replaced,
    Unlinked,
    MetadataOnly,
}

/// <summary>
/// The Torrents page's Status and Problems filters: their values as they appear in the address, their labels, and
/// which torrents they let through. An unknown value lets everything through, like an empty one.
/// </summary>
public static class TorrentListFilters
{
    public static readonly IReadOnlyList<(string Value, string Label)> StatusOptions =
    [
        ("linked", "Linked"),
        ("not-linked", "Not linked"),
        ("waiting", "Waiting to be linked"),
        ("replaced", "Replaced"),
        ("unlinked", "Unlinked by you"),
        ("metadata-only", "Metadata only"),
    ];

    public static readonly IReadOnlyList<(string Value, string Label)> ProblemOptions =
    [
        ("any", "Any problem"),
        ("mismatch", "Library mismatch"),
        ("not-in-client", "Not in qBittorrent"),
    ];

    /// <summary>
    /// A torrent with a library folder counts as linked whatever its markers say, since its files are in the library.
    /// One without is a metadata-only record, unlinked by the user, replaced by a better copy, or still waiting.
    /// </summary>
    public static TorrentLinkStatus StatusOf(TorrentEntity torrent) =>
        MetadataOnlyTorrentId.IsMetadataOnly(torrent.Id) ? TorrentLinkStatus.MetadataOnly
        : torrent.LibraryPath is not null ? TorrentLinkStatus.Linked
        : torrent.UnlinkedAt is not null ? TorrentLinkStatus.Unlinked
        : torrent.ReplacedWithTorrentId is not null ? TorrentLinkStatus.Replaced
        : TorrentLinkStatus.Waiting;

    public static bool MatchesStatus(TorrentEntity torrent, string? status) => status switch
    {
        "linked" => StatusOf(torrent) == TorrentLinkStatus.Linked,
        "not-linked" => StatusOf(torrent) != TorrentLinkStatus.Linked,
        "waiting" => StatusOf(torrent) == TorrentLinkStatus.Waiting,
        "replaced" => StatusOf(torrent) == TorrentLinkStatus.Replaced,
        "unlinked" => StatusOf(torrent) == TorrentLinkStatus.Unlinked,
        "metadata-only" => StatusOf(torrent) == TorrentLinkStatus.MetadataOnly,
        _ => true,
    };

    public static bool MatchesProblem(TorrentEntity torrent, string? problem) => problem switch
    {
        "any" => HasMismatch(torrent) || IsNotInClient(torrent),
        "mismatch" => HasMismatch(torrent),
        "not-in-client" => IsNotInClient(torrent),
        _ => true,
    };

    private static bool HasMismatch(TorrentEntity torrent) => torrent.LibraryMismatchState is not null;

    private static bool IsNotInClient(TorrentEntity torrent) => torrent.ClientStatus == ClientStatuses.NotInClient;
}
