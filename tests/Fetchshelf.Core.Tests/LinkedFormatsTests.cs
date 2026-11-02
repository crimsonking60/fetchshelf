using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LinkedFormatsTests
{
    [Fact]
    public void Choose_AudioAndEbookFiles_PicksTheBestListedOfEach()
    {
        var (audio, ebook) = LinkedFormats.Choose(["m4b", "mp3"], ["epub", "pdf"], ["disc1/01.mp3", "book.M4B", "book.pdf", "book.epub", "cover.jpg"]);

        Assert.Equal("m4b", audio);
        Assert.Equal("epub", ebook);
    }

    [Fact]
    public void Choose_NoListedFormat_ReturnsNullForThatSide()
    {
        var (audio, ebook) = LinkedFormats.Choose(["m4b"], ["epub"], ["book.flac", "book.epub"]);

        Assert.Null(audio);
        Assert.Equal("epub", ebook);
    }

    [Theory]
    [InlineData("disc1/book.M4B", true)]
    [InlineData("book.epub", true)]
    [InlineData("book.mp3", false)]
    [InlineData("cover.jpg", false)]
    public void IsChosen_FileName_MatchesEitherChosenExtensionIgnoringCase(string fileName, bool expected) =>
        Assert.Equal(expected, LinkedFormats.IsChosen(fileName, "m4b", "epub"));

    [Fact]
    public void IsChosen_NothingChosen_ReturnsFalse() =>
        Assert.False(LinkedFormats.IsChosen("book.m4b", null, null));
}
