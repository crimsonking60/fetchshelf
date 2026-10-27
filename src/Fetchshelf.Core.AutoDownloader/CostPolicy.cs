namespace Fetchshelf.Core.AutoDownloader;

/// <summary>Derives a selected torrent's cost decision, Fetchshelf-spec.md §7.4.</summary>
public static class CostPolicy
{
    /// <summary>
    /// VIP, personal-freeleech, and global-freeleech always win over the configured cost. Otherwise
    /// `wedge`/`try_wedge` map to UseWedge/TryWedge, any other configured cost maps to Ratio - except
    /// `free`, which returns null (rejecting the candidate) when none of the three free routes apply.
    /// </summary>
    public static SelectedTorrentCost? Derive(
        bool isVip, bool isPersonalFreeleech, bool isGlobalFreeleech, ConfiguredCost configuredCost)
    {
        if (isVip)
        {
            return SelectedTorrentCost.Vip;
        }

        if (isPersonalFreeleech)
        {
            return SelectedTorrentCost.PersonalFreeleech;
        }

        if (isGlobalFreeleech)
        {
            return SelectedTorrentCost.GlobalFreeleech;
        }

        return configuredCost switch
        {
            ConfiguredCost.Free => null,
            ConfiguredCost.Wedge => SelectedTorrentCost.UseWedge,
            ConfiguredCost.TryWedge => SelectedTorrentCost.TryWedge,
            _ => SelectedTorrentCost.Ratio,
        };
    }
}
