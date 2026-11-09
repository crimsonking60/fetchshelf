using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class XmlSafeTextTests
{
    [Theory]
    [InlineData("Page one.\fPage two.", "Page one.Page two.")]
    [InlineData("A\u0000B\u001FC", "ABC")]
    [InlineData("Tabs\t, newlines\n and returns\r stay", "Tabs\t, newlines\n and returns\r stay")]
    [InlineData("Søk & finn 📚", "Søk & finn 📚")]
    [InlineData("\uFFFE\uFFFF", "")]
    public void Clean_LeavesOutOnlyWhatXmlCannotHold(string text, string expected) =>
        Assert.Equal(expected, XmlSafeText.Clean(text));

    [Fact]
    public void Clean_LoneSurrogate_IsLeftOut() =>
        Assert.Equal("A lone  surrogate", XmlSafeText.Clean("A lone \uD83D surrogate"));

    [Fact]
    public void Clean_CleanText_IsReturnedAsItIs()
    {
        var text = "Nothing to clean";

        Assert.Same(text, XmlSafeText.Clean(text));
    }
}
