using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class CoverFileTests
{
    [Theory]
    [InlineData(new[] { "01.mp3", "metadata.json" }, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "01.mp3", "cover.jpg" }, false)]
    [InlineData(new[] { "book.epub", "folder.JPEG" }, false)]
    [InlineData(new[] { "01.m4b", "The Way of Kings.png" }, false)]
    [InlineData(new[] { "01.m4b", "art.webp" }, false)]
    [InlineData(new[] { "01.m4b", "notes.pdf" }, true)]
    public void IsNeeded_OnlyWithoutAnyImage(string[] fileNames, bool expected) =>
        Assert.Equal(expected, CoverFile.IsNeeded(fileNames));

    [Theory]
    [InlineData("image/jpeg", "cover.jpg")]
    [InlineData("IMAGE/JPEG", "cover.jpg")]
    [InlineData("image/png", "cover.png")]
    [InlineData("image/webp", "cover.webp")]
    [InlineData("image/gif", null)]
    [InlineData("text/html", null)]
    public void NameFor_JpegPngAndWebpOnly(string contentType, string? expected) =>
        Assert.Equal(expected, CoverFile.NameFor(contentType));

    [Fact]
    public void Names_AreEveryNameACoverIsWrittenUnder() =>
        Assert.Equal(["cover.jpg", "cover.png", "cover.webp"], CoverFile.Names);
}
