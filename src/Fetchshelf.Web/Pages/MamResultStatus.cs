namespace Fetchshelf.Web.Pages;

/// <summary>Where a MAM search result stands in Fetchshelf's own pipeline, most-advanced state wins.</summary>
public enum MamResultStatus
{
    None,
    Selected,

    /// <summary>Added to qBittorrent, and not known to have finished: its stored client status is empty or a download stage.</summary>
    Downloading,

    /// <summary>Finished in qBittorrent, or owned without a download (metadata only), but not in a library.</summary>
    Downloaded,
    Linked,
}
