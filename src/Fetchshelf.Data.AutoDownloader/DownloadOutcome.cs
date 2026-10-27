namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What happened to one selected torrent processed by <see cref="DownloadRunner"/>, Fetchshelf-spec.md §7.3.</summary>
public enum DownloadOutcome
{
    NoUnsatSlot,
    OverRatioBudget,
    WedgeUnavailable,
    LinkStillStaleAfterRefresh,
    AlreadyFinishedInClient,
    AlreadyDownloadingInClient,
    AlreadyLinked,
    NoLongerFree,
    Started,
    AccountBlocked,
}
