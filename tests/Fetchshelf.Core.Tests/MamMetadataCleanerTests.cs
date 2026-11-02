using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class MamMetadataCleanerTests
{
    [Fact]
    public void DecodeHtmlEntities_AmpersandEntity_IsDecoded()
    {
        var result = MamMetadataCleaner.DecodeHtmlEntities("Smith &amp; Jones");

        Assert.Equal("Smith & Jones", result);
    }

    [Fact]
    public void NormalizeNameCasing_AllCapsWordLongerThanThree_IsLowerCasedAfterFirstLetter()
    {
        var result = MamMetadataCleaner.NormalizeNameCasing("SMITH");

        Assert.Equal("Smith", result);
    }

    [Fact]
    public void NormalizeNameCasing_AllLowercaseWordLongerThanThree_HasFirstLetterCapitalized()
    {
        var result = MamMetadataCleaner.NormalizeNameCasing("smith");

        Assert.Equal("Smith", result);
    }

    [Theory]
    [InlineData("Jo")]
    [InlineData("Ann")]
    public void NormalizeNameCasing_WordOfThreeCharactersOrFewer_IsLeftUnchanged(string word)
    {
        Assert.Equal(word, MamMetadataCleaner.NormalizeNameCasing(word));
    }

    [Fact]
    public void NormalizeNameCasing_MixedCaseWord_IsLeftUnchanged()
    {
        var result = MamMetadataCleaner.NormalizeNameCasing("McSmith");

        Assert.Equal("McSmith", result);
    }

    [Fact]
    public void StripSeriesSuffix_TrailingDigitalMarker_IsRemoved()
    {
        var result = MamMetadataCleaner.StripSeriesSuffix("Some Series (Digital)");

        Assert.Equal("Some Series", result);
    }

    [Fact]
    public void StripSeriesSuffix_TrailingLightNovelMarker_IsRemoved()
    {
        var result = MamMetadataCleaner.StripSeriesSuffix("Some Series (Light Novel)");

        Assert.Equal("Some Series", result);
    }

    [Fact]
    public void ReclassifyMediaType_EbookWithAudioLegacyCategory_BecomesAudiobook()
    {
        var result = MamMetadataCleaner.ReclassifyMediaType(MediaType.Ebook, LegacyCategory.AudiobookFantasy);

        Assert.Equal(MediaType.Audiobook, result);
    }

    [Fact]
    public void ReclassifyMediaType_EbookWithEbookLegacyCategory_StaysEbook()
    {
        var result = MamMetadataCleaner.ReclassifyMediaType(MediaType.Ebook, LegacyCategory.EbookFantasy);

        Assert.Equal(MediaType.Ebook, result);
    }

    [Fact]
    public void StripAuthorReference_TrailingByAuthor_IsRemoved()
    {
        var result = MamMetadataCleaner.StripAuthorReference("Great Book by Jane Doe", ["Jane Doe"], MediaType.Audiobook);

        Assert.Equal("Great Book", result);
    }

    [Fact]
    public void StripAuthorReference_TrailingDashAuthor_IsRemoved()
    {
        var result = MamMetadataCleaner.StripAuthorReference("Great Book - Jane Doe", ["Jane Doe"], MediaType.Ebook);

        Assert.Equal("Great Book", result);
    }

    [Fact]
    public void StripAuthorReference_LeadingAuthorDash_IsRemoved()
    {
        var result = MamMetadataCleaner.StripAuthorReference("Jane Doe - Great Book", ["Jane Doe"], MediaType.Ebook);

        Assert.Equal("Great Book", result);
    }

    [Fact]
    public void StripAuthorReference_PeriodicalWithOneAuthor_LeavesTitleUnchanged()
    {
        var title = "Great Magazine by Jane Doe";

        var result = MamMetadataCleaner.StripAuthorReference(title, ["Jane Doe"], MediaType.PeriodicalEbook);

        Assert.Equal(title, result);
    }

    [Theory]
    [InlineData("Great Book: A Novel", "Great Book")]
    [InlineData("Great Book: A Memoir", "Great Book")]
    [InlineData("Great Book (unabridged)", "Great Book")]
    [InlineData("Great Book [epub]", "Great Book")]
    [InlineData("Great Book (digital)", "Great Book")]
    [InlineData("Great Book - 123.epub", "Great Book")]
    public void RemoveTitleNoise_KnownPatterns_AreRemoved(string input, string expected)
    {
        Assert.Equal(expected, MamMetadataCleaner.RemoveTitleNoise(input));
    }
}
