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

    [Theory]
    [InlineData("Война и мир", "война и мир")]
    [InlineData("Преступление и наказание", "преступление и наказание")]
    [InlineData("Ὀδύσσεια", "ὀδύσσεια")]
    [InlineData("מלחמה ושלום", "מלחמה ושלום")]
    [InlineData("ألف ليلة وليلة", "ألف ليلة وليلة")]
    [InlineData("三体", "三体")]
    [InlineData("ノルウェイの森", "ノルウェイの森")]
    [InlineData("채식주의자", "채식주의자")]
    public void Normalize_TitleInAnotherScript_KeepsItsLetters(string title, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Normalize(title));
    }

    [Fact]
    public void Normalize_TwoDifferentTitlesInOneScript_KeyDifferently()
    {
        Assert.NotEqual(TitleNormalizer.Normalize("Война и мир"), TitleNormalizer.Normalize("Анна Каренина"));
    }

    [Theory]
    [InlineData("Мой дядя", "Мои дядя")]
    [InlineData("かがみ", "かかみ")]
    public void Normalize_LettersThatDifferOnlyByAMarkInAnotherScript_StayDifferent(string a, string b)
    {
        Assert.NotEqual(TitleNormalizer.Normalize(a), TitleNormalizer.Normalize(b));
    }

    [Fact]
    public void Normalize_MixedScriptTitle_KeepsBothParts()
    {
        Assert.Equal("война и мир war and peace", TitleNormalizer.Normalize("Война и мир (War and Peace)"));
    }

    [Theory]
    [InlineData("Søren", "soren")]
    [InlineData("Ægir", "aegir")]
    [InlineData("Œuvres", "oeuvres")]
    [InlineData("Straße", "strasse")]
    [InlineData("Łódź", "lodz")]
    [InlineData("Đorđe", "dorde")]
    [InlineData("Þór", "thor")]
    [InlineData("Eðda", "edda")]
    public void Normalize_LatinLettersWithoutAccentFreeForm_AreSpelledOut(string title, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Normalize(title));
    }

    [Fact]
    public void Normalize_IdeographOutsideTheBasicPlane_IsKeptWhole()
    {
        Assert.Equal("𠮷野家", TitleNormalizer.Normalize("𠮷野家"));
    }

    [Fact]
    public void Normalize_DecomposedAccent_FoldsLikeTheComposedOne()
    {
        Assert.Equal(TitleNormalizer.Normalize("Café"), TitleNormalizer.Normalize("Cafe\u0301"));
    }

    [Theory]
    [InlineData("?!", "?!")]
    [InlineData("  ★★★  ", "★★★")]
    public void Normalize_TitleOfOnlySymbols_KeysAsItself(string title, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Normalize(title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankTitle_KeysAsEmpty(string title)
    {
        Assert.Equal(string.Empty, TitleNormalizer.Normalize(title));
    }
}
