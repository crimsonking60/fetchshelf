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

    [Theory]
    [InlineData("Exploitation Film Posters by Graham Marsh", "Exploitation Film Posters", "Graham Marsh")]
    [InlineData("The Hobbit by J.R.R. Tolkien", "The Hobbit", "J.R.R. Tolkien")]
    [InlineData("The Fall of Paris by Robert van Gulik", "The Fall of Paris", "Robert van Gulik")]
    [InlineData("Stand by Me and Others by Seán O'Brien", "Stand by Me and Others", "Seán O'Brien")]
    [InlineData("Collected Stories by Ursula K. Le Guin", "Collected Stories", "Ursula K. Le Guin")]
    public void SplitAuthorFromTitle_TrailingByName_SplitsTitleAndAuthor(string title, string expectedTitle, string expectedAuthor)
    {
        var result = MamMetadataCleaner.SplitAuthorFromTitle(title);

        Assert.Equal((expectedTitle, expectedAuthor), result);
    }

    [Theory]
    [InlineData("Stand by Me")]
    [InlineData("Death by 1000 Cuts")]
    [InlineData("Murder by the Book")]
    [InlineData("Posters by Graham Marsh and Tony Nourmand")]
    [InlineData("by Graham Marsh")]
    [InlineData("Exploitation Film Posters")]
    [InlineData("Lost by van Gulik Robert")]
    public void SplitAuthorFromTitle_NoClearName_ReturnsNull(string title)
    {
        Assert.Null(MamMetadataCleaner.SplitAuthorFromTitle(title));
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
