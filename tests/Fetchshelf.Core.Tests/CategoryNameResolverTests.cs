using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class CategoryNameResolverTests
{
    [Theory]
    [InlineData("Action", LegacyCategory.AudiobookActionAdventure)]
    [InlineData("action/adventure", LegacyCategory.AudiobookActionAdventure)]
    [InlineData("FOOD", LegacyCategory.AudiobookFood)]
    [InlineData("sf", LegacyCategory.AudiobookScienceFiction)]
    [InlineData("science fiction", LegacyCategory.AudiobookScienceFiction)]
    [InlineData("self-help", LegacyCategory.AudiobookSelfHelp)]
    [InlineData("ya", LegacyCategory.AudiobookYoungAdult)]
    [InlineData("home/garden", LegacyCategory.AudiobookHomeGarden)]
    [InlineData("pol/soc/relig", LegacyCategory.AudiobookPolSocRelig)]
    public void Audio_ResolvesTolerantSpellings(string name, LegacyCategory expected)
    {
        Assert.True(CategoryNameResolver.TryResolve(FilterMainType.Audio, name, out var category));
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData("comics", LegacyCategory.EbookComicsGraphicNovels)]
    [InlineData("comics/graphic novels", LegacyCategory.EbookComicsGraphicNovels)]
    [InlineData("illusion/magic", LegacyCategory.EbookIllusionMagic)]
    [InlineData("magazines/newspapers", LegacyCategory.EbookMagazinesNewspapers)]
    [InlineData("mixed collections", LegacyCategory.EbookMixedCollections)]
    public void Ebook_ResolvesTolerantSpellings(string name, LegacyCategory expected)
    {
        Assert.True(CategoryNameResolver.TryResolve(FilterMainType.Ebook, name, out var category));
        Assert.Equal(expected, category);
    }

    [Fact]
    public void Musicology_ResolvesExactName()
    {
        Assert.True(CategoryNameResolver.TryResolve(
            FilterMainType.Musicology, "Sheet Collection MP3", out var category));
        Assert.Equal(LegacyCategory.MusicologySheetCollectionMp3, category);
    }

    [Theory]
    [InlineData("factual")]
    [InlineData("documentary")]
    [InlineData("factual/documentary")]
    public void Radio_ResolvesTolerantSpellings(string name)
    {
        Assert.True(CategoryNameResolver.TryResolve(FilterMainType.Radio, name, out var category));
        Assert.Equal(LegacyCategory.RadioFactualDocumentary, category);
    }

    [Fact]
    public void UnrecognizedName_ReturnsFalse()
    {
        Assert.False(CategoryNameResolver.TryResolve(FilterMainType.Audio, "not-a-category", out _));
    }

    [Fact]
    public void NameFromWrongMainType_ReturnsFalse()
    {
        // "comics" is only a valid Ebook category name, not Audio.
        Assert.False(CategoryNameResolver.TryResolve(FilterMainType.Audio, "comics", out _));
    }
}
