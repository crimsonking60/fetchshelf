using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class EditionExtractorTests
{
    [Theory]
    [InlineData("Some Book 2nd Edition", "2nd Edition", 2)]
    [InlineData("Some Book (First edition)", "First Edition", 1)]
    [InlineData("Some Book [Second Canadian Edition]", "Second Canadian Edition", 2)]
    [InlineData("Some Book - 25th Anniversary Edition:", "25th Anniversary Edition", 25)]
    [InlineData("Some Book 2012 Edition", "2012 Edition", 0)]
    [InlineData("Some Book 3rd ed.", "3rd Edition", 3)]
    [InlineData("Some Book UK Edition", "Uk Edition", 0)]
    public void TryExtract_DocumentedExamples_AreRecognized(string text, string expectedLabel, int expectedNumber)
    {
        var match = EditionExtractor.TryExtract(text);

        Assert.NotNull(match);
        Assert.Equal(expectedLabel, match!.Label);
        Assert.Equal(expectedNumber, match.Number);
    }

    [Fact]
    public void TryExtract_NoEditionPhrase_ReturnsNull()
    {
        var match = EditionExtractor.TryExtract("Some Book With No Edition Marker At All");

        Assert.Null(match);
    }

    [Fact]
    public void RemoveFromTitle_TextAfterPhraseBecomesSubtitle()
    {
        var title = "Foo - 25th Anniversary Edition: The Return";
        var match = EditionExtractor.TryExtract(title)!;

        var result = EditionExtractor.RemoveFromTitle(title, match);

        Assert.Equal("Foo: The Return", result);
    }

    [Fact]
    public void RemoveFromTitle_NoTrailingText_LeavesOnlyBeforePart()
    {
        var title = "Some Book (First edition)";
        var match = EditionExtractor.TryExtract(title)!;

        var result = EditionExtractor.RemoveFromTitle(title, match);

        Assert.Equal("Some Book", result);
    }
}
