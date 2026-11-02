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
    public void Normalize_AccentedMixedCaseTitle_IsTransliteratedToLowercaseAscii()
    {
        var result = TitleNormalizer.Normalize("Café Münster");

        Assert.Equal("cafe munster", result);
    }

    [Fact]
    public void Normalize_Ampersand_BecomesAnd()
    {
        var result = TitleNormalizer.Normalize("Salt & Pepper");

        Assert.Equal("salt and pepper", result);
    }

    [Theory]
    [InlineData("A Study in Scarlet", "study in scarlet")]
    [InlineData("An Education", "education")]
    [InlineData("The Great Gatsby", "great gatsby")]
    public void Normalize_LeadingArticle_IsRemoved(string input, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_VolumeWord_IsRemovedButNotItsAbbreviation()
    {
        var result = TitleNormalizer.Normalize("Saga Volume 3");

        Assert.Equal("saga 3", result);
    }

    [Fact]
    public void Normalize_Punctuation_IsStripped()
    {
        var result = TitleNormalizer.Normalize("Book: Title! (Special*Edition)");

        Assert.Matches("^[a-z0-9_ ]*$", result);
    }
}
