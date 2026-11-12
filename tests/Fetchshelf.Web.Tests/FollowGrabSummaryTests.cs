using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Pages.Following;

namespace Fetchshelf.Web.Tests;

public class FollowGrabSummaryTests
{
    [Fact]
    public void FreeEbooksInOneLanguage_SaySoInPlainWords() =>
        Assert.Equal(
            "Only books that are free (freeleech, VIP or personal freeleech), ebooks only, in English.",
            FollowGrabSummary.Describe(new GrabEntry { Categories = new CategoriesConfig { Audio = CategoryListOrBool.None }, Languages = ["english"] }));

    [Theory]
    [InlineData(ConfiguredCost.Wedge, "Free books, and others with a freeleech wedge.")]
    [InlineData(ConfiguredCost.TryWedge, "Free books, others with a freeleech wedge while there are wedges to spare, and otherwise paid with ratio.")]
    [InlineData(ConfiguredCost.Ratio, "Any book: free ones cost nothing, others are paid with ratio.")]
    public void EachCost_HasItsOwnWords(ConfiguredCost cost, string expected) =>
        Assert.Equal(expected, FollowGrabSummary.Describe(new GrabEntry { Cost = cost }));

    [Fact]
    public void AudiobooksInSeveralLanguages() =>
        Assert.Equal(
            "Free books, and others with a freeleech wedge, audiobooks only, in English, German or Norwegian.",
            FollowGrabSummary.Describe(new GrabEntry
            {
                Cost = ConfiguredCost.Wedge,
                Categories = new CategoriesConfig { Ebook = CategoryListOrBool.None },
                Languages = ["English", "German", "Norwegian"],
            }));

    [Fact]
    public void OtherFilters_AreMentionedNotSpelledOut()
    {
        Assert.EndsWith(", if the block's other filters allow it.", FollowGrabSummary.Describe(new GrabEntry { MinSeeders = 3 }));
        Assert.EndsWith(
            ", if the block's other filters allow it.",
            FollowGrabSummary.Describe(new GrabEntry { Categories = new CategoriesConfig { Audio = CategoryListOrBool.Of(["Fantasy"]) } }));
        Assert.DoesNotContain("other filters", FollowGrabSummary.Describe(new GrabEntry()));
    }

    [Fact]
    public void SeveralBlocks_AreDescribedInTheirOrder() =>
        Assert.Equal(
            ["Only books that are free (freeleech, VIP or personal freeleech).", "Free books, and others with a freeleech wedge."],
            FollowGrabSummary.Describe([new GrabEntry(), new GrabEntry { Cost = ConfiguredCost.Wedge }]));
}
