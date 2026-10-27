using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class LanguageNameResolverTests
{
    [Theory]
    [InlineData("English", Language.English)]
    [InlineData("brazilian", Language.BrazilianPortuguese)]
    [InlineData("BP", Language.BrazilianPortuguese)]
    [InlineData("brazilian portuguese", Language.BrazilianPortuguese)]
    [InlineData("castilian", Language.CastilianSpanish)]
    [InlineData("castilian spanish", Language.CastilianSpanish)]
    [InlineData("scottish", Language.ScottishGaelic)]
    [InlineData("scottish gaelic", Language.ScottishGaelic)]
    [InlineData("gaelic", Language.ScottishGaelic)]
    [InlineData("greek, ancient", Language.AncientGreek)]
    [InlineData("ancient greek", Language.AncientGreek)]
    public void ResolvesDocumentedAliases(string name, Language expected)
    {
        Assert.True(LanguageNameResolver.TryResolve(name, out var language));
        Assert.Equal(expected, language);
    }

    [Fact]
    public void UnrecognizedName_ReturnsFalse()
    {
        Assert.False(LanguageNameResolver.TryResolve("not-a-language", out _));
    }
}
