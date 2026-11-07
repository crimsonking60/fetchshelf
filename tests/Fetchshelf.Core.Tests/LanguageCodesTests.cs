using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class LanguageCodesTests
{
    [Fact]
    public void For_EveryLanguageButOther_HasAThreeLetterCode()
    {
        foreach (var language in Enum.GetValues<Language>().Where(l => l != Language.Other))
        {
            var code = LanguageCodes.For(language);
            Assert.True(code is { Length: 3 } && code.All(char.IsAsciiLetterLower), $"{language}: {code}");
        }
    }

    [Fact]
    public void For_Other_IsNull() => Assert.Null(LanguageCodes.For(Language.Other));

    [Theory]
    [InlineData(Language.English, "eng")]
    [InlineData(Language.German, "deu")]
    [InlineData(Language.BrazilianPortuguese, "por")]
    [InlineData(Language.CastilianSpanish, "spa")]
    [InlineData(Language.Farsi, "fas")]
    public void For_KnownLanguage_ReturnsItsCode(Language language, string code) =>
        Assert.Equal(code, LanguageCodes.For(language));
}
