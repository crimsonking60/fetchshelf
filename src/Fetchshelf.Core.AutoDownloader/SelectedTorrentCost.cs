namespace Fetchshelf.Core.AutoDownloader;

/// <summary>A selected torrent's resolved cost decision.</summary>
public enum SelectedTorrentCost
{
    Vip,
    PersonalFreeleech,
    GlobalFreeleech,
    UseWedge,
    TryWedge,
    Ratio,
}

/// <summary>Rules about <see cref="SelectedTorrentCost"/> values as a whole.</summary>
public static class SelectedTorrentCosts
{
    /// <summary>
    /// The costs a queued selection can be changed to by hand. The free costs only describe what MAM reported when the
    /// torrent was selected; picking one by hand would let a paid torrent skip the ratio budget, so they are never offered.
    /// </summary>
    public static readonly IReadOnlyList<SelectedTorrentCost> ChangeableByHand =
        [SelectedTorrentCost.Ratio, SelectedTorrentCost.TryWedge, SelectedTorrentCost.UseWedge];
}

/// <summary>The configured `cost` setting of an `[[autograb]]` or `[[snatchlist]]`.</summary>
public enum ConfiguredCost
{
    Free,
    Wedge,
    TryWedge,
    Ratio,
    MetadataOnly,
    MetadataOnlyAdd,
}
