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
