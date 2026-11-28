namespace Fetchshelf.Core.AutoDownloader.Tests;

public class AuthorNameKeyTests
{
    [Theory]
    [InlineData("R.T. de Vries", "r t de vries")]
    [InlineData("Henry P. Dowd, Jr.", "henry p dowd")]
    [InlineData("Henry Dowd III", "henry dowd")]
    [InlineData("  Ann-Marie   Quill ", "ann marie quill")]
    [InlineData("Jr.", "jr")]
    public void Of_LowerCaseWithoutPunctuationOrSuffix(string name, string expected) =>
        Assert.Equal(expected, AuthorNameKey.Of(name));
}
