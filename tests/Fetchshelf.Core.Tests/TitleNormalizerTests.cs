using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class TitleNormalizerTests
{
    [Fact]
    public void Normalize_RemovesLeadingArticle_VolumeWord_AndInvalidCharacters()
    {
        var result = TitleNormalizer.Normalize("The Hobbit, Vol. 2");

        Assert.Equal("hobbit 2", result);
    }

    [Fact]
    public void Normalize_TransliteratesToAsciiAndLowercases()
    {
        var result = TitleNormalizer.Normalize("Café Münster");

        Assert.Equal("cafe munster", result);
    }

    [Fact]
    public void Normalize_ReplacesAmpersandWithAnd()
    {
        var result = TitleNormalizer.Normalize("Salt & Pepper");

        Assert.Equal("salt and pepper", result);
    }

    [Theory]
    [InlineData("A Study in Scarlet", "study in scarlet")]
    [InlineData("An Education", "education")]
    [InlineData("The Great Gatsby", "great gatsby")]
    public void Normalize_RemovesLeadingArticles(string input, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_RemovesVolumeWordWithoutAbbreviation()
    {
        var result = TitleNormalizer.Normalize("Saga Volume 3");

        Assert.Equal("saga 3", result);
    }

    [Fact]
    public void Normalize_StripsCharactersOutsideLettersDigitsUnderscoreSpace()
    {
        var result = TitleNormalizer.Normalize("Book: Title! (Special*Edition)");

        Assert.Matches("^[a-z0-9_ ]*$", result);
    }
}
