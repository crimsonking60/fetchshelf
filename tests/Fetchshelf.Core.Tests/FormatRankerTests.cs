using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class FormatRankerTests
{
    private static readonly IReadOnlyList<string> Preference = ["m4b", "m4a", "mp3"];

    [Fact]
    public void Rank_BestRankedExtensionPresent_ReturnsZero()
    {
        Assert.Equal(0, FormatRanker.Rank(Preference, ["mp3", "m4b"]));
    }

    [Fact]
    public void Rank_OnlyWorseRankedExtensionPresent_ReturnsItsPosition()
    {
        Assert.Equal(2, FormatRanker.Rank(Preference, ["mp3"]));
    }

    [Fact]
    public void Rank_NoListedExtensionPresent_ReturnsNotPresent()
    {
        Assert.Equal(FormatRanker.NotPresent, FormatRanker.Rank(Preference, ["flac"]));
    }

    [Fact]
    public void SelectBestExtension_ChoosesLowestRankedPresentExtension()
    {
        Assert.Equal("m4b", FormatRanker.SelectBestExtension(Preference, ["mp3", "m4b"]));
    }

    [Fact]
    public void SelectBestExtension_NoneListedPresent_ReturnsNull()
    {
        Assert.Null(FormatRanker.SelectBestExtension(Preference, ["flac"]));
    }
}
