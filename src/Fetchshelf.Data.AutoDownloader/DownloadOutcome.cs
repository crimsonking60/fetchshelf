namespace Fetchshelf.Data.AutoDownloader;

/// <summary>What happened to one selected torrent processed by <see cref="DownloadRunner"/>.</summary>
public enum DownloadOutcome
{
    NoUnsatSlot,
    OverRatioBudget,
    WedgeUnavailable,
    LinkStillStaleAfterRefresh,

    /// <summary>MAM no longer has the torrent; the selection was taken out of the queue.</summary>
    NoLongerOnMam,
    AlreadyFinishedInClient,
    AlreadyDownloadingInClient,
    AlreadyLinked,
    NoLongerFree,
    Started,
}
