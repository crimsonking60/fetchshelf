using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Pages.Following;

namespace Fetchshelf.Web.Tests;

public class FollowGrabSummaryTests
{
    private static readonly CategoriesConfig EbooksOnly = new() { Audio = CategoryListOrBool.None };

    private static readonly CategoriesConfig AudiobooksOnly = new() { Ebook = CategoryListOrBool.None };

    [Fact]
    public void ARule_SaysWhichBooksAndThenHowTheyArePaidFor() =>
        Assert.Equal(
            "Ebooks in English: free ones, and others paid with ratio.",
            FollowGrabSummary.Describe(new GrabEntry { Cost = ConfiguredCost.Ratio, Categories = EbooksOnly, Languages = ["english"] }));

    [Theory]
    [InlineData(ConfiguredCost.Free, "only free ones (freeleech, VIP or personal freeleech)")]
    [InlineData(ConfiguredCost.Wedge, "free ones, and others with a freeleech wedge")]
    [InlineData(ConfiguredCost.TryWedge, "free ones, and others with a freeleech wedge while there are wedges to spare, otherwise paid with ratio")]
    [InlineData(ConfiguredCost.Ratio, "free ones, and others paid with ratio")]
    public void EachCost_FillsTheSecondHalf(ConfiguredCost cost, string expected) =>
        Assert.Equal($"Audiobooks: {expected}.", FollowGrabSummary.Describe(new GrabEntry { Cost = cost, Categories = AudiobooksOnly }));

    [Fact]
    public void ABlockTakingBothTypes_NamesThemBoth_AndItsOwnLanguages() =>
        Assert.Equal(
            "Ebooks and audiobooks in German or French: only free ones (freeleech, VIP or personal freeleech).",
            FollowGrabSummary.Describe(new GrabEntry { Languages = ["German", "French"] }));

    [Fact]
    public void ThreeLanguages_AreJoinedWithCommasAndOr() =>
        Assert.StartsWith("Audiobooks in English, German or Norwegian: ", FollowGrabSummary.Describe(
            new GrabEntry { Categories = AudiobooksOnly, Languages = ["English", "German", "Norwegian"] }));

    [Fact]
    public void OtherFilters_AreMentionedWithTheBooks_NotSpelledOut()
    {
        Assert.Equal(
            "Ebooks in English that pass the block's other filters: free ones, and others with a freeleech wedge.",
            FollowGrabSummary.Describe(new GrabEntry { Cost = ConfiguredCost.Wedge, Categories = EbooksOnly, Languages = ["English"], MinSeeders = 3 }));
        Assert.Contains(
            "that pass the block's other filters",
            FollowGrabSummary.Describe(new GrabEntry { Categories = new CategoriesConfig { Audio = CategoryListOrBool.Of(["Fantasy"]) } }));
        Assert.DoesNotContain("other filters", FollowGrabSummary.Describe(new GrabEntry()));
    }

    [Fact]
    public void SeveralBlocks_AreDescribedInTheirOrder() =>
        Assert.Equal(
            ["Ebooks: free ones, and others paid with ratio.", "Audiobooks: free ones, and others with a freeleech wedge."],
            FollowGrabSummary.Describe([
                new GrabEntry { Cost = ConfiguredCost.Ratio, Categories = EbooksOnly },
                new GrabEntry { Cost = ConfiguredCost.Wedge, Categories = AudiobooksOnly },
            ]));
}
