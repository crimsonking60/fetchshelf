namespace Fetchshelf.Core.AutoDownloader.Tests;

public class CostPolicyTests
{
    [Fact]
    public void Vip_OverridesConfiguredRatioCost()
    {
        var cost = CostPolicy.Derive(isVip: true, isPersonalFreeleech: false, isGlobalFreeleech: false, ConfiguredCost.Ratio);

        Assert.Equal(SelectedTorrentCost.Vip, cost);
    }

    [Fact]
    public void PersonalFreeleech_OverridesConfiguredCost_WhenNotVip()
    {
        var cost = CostPolicy.Derive(isVip: false, isPersonalFreeleech: true, isGlobalFreeleech: false, ConfiguredCost.Wedge);

        Assert.Equal(SelectedTorrentCost.PersonalFreeleech, cost);
    }

    [Fact]
    public void GlobalFreeleech_OverridesConfiguredCost_WhenNotVipOrPersonalFreeleech()
    {
        var cost = CostPolicy.Derive(isVip: false, isPersonalFreeleech: false, isGlobalFreeleech: true, ConfiguredCost.Wedge);

        Assert.Equal(SelectedTorrentCost.GlobalFreeleech, cost);
    }

    [Theory]
    [InlineData(ConfiguredCost.Wedge, SelectedTorrentCost.UseWedge)]
    [InlineData(ConfiguredCost.TryWedge, SelectedTorrentCost.TryWedge)]
    [InlineData(ConfiguredCost.Ratio, SelectedTorrentCost.Ratio)]
    public void NonFreeTorrent_MapsConfiguredCost(ConfiguredCost configured, SelectedTorrentCost expected)
    {
        var cost = CostPolicy.Derive(isVip: false, isPersonalFreeleech: false, isGlobalFreeleech: false, configured);

        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ConfiguredFree_RejectsNonFreeTorrent()
    {
        var cost = CostPolicy.Derive(isVip: false, isPersonalFreeleech: false, isGlobalFreeleech: false, ConfiguredCost.Free);

        Assert.Null(cost);
    }
}
