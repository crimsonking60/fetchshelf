namespace Fetchshelf.Core.Metadata;

/// <summary>
/// How details edited by hand survive fresh details from MAM: link runs, snatchlists, Refresh and relink, and an autograb,
/// list, Follow or Search page download that comes across a torrent already in the library.
/// </summary>
public static class HandEdits
{
    public static bool AreIn(TorrentMetadata metadata) => metadata.MetadataSource == MetadataSource.Manual;

    /// <summary>
    /// The details to keep: hand-edited ones stay, with only their VIP status taken from <paramref name="fresh"/>;
    /// otherwise the fresh details replace the stored ones.
    /// </summary>
    public static TorrentMetadata KeepOver(TorrentMetadata stored, TorrentMetadata fresh) =>
        AreIn(stored) ? stored with { VipStatus = fresh.VipStatus } : fresh;
}
