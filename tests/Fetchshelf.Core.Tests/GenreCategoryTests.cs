using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class GenreCategoryTests
{
    // Real tracker ids.
    [Theory]
    [InlineData(GenreCategory.Action, 1)]
    [InlineData(GenreCategory.SheetCollection, 47)]
    [InlineData(GenreCategory.SheetCollectionMp3, 48)]
    [InlineData(GenreCategory.Sports, 49)]
    [InlineData(GenreCategory.DramaPlays, 60)]
    [InlineData(GenreCategory.Occult, 61)]
    [InlineData(GenreCategory.SliceOfLife, 62)]
    [InlineData(GenreCategory.Military, 63)]
    public void NumericValue_EachCategory_MatchesRealTrackerId(GenreCategory category, int expectedId)
    {
        Assert.Equal(expectedId, (int)category);
    }

    // 26 and 48 are MAM's Erotica/Sexual Content and Accompanying music, whatever their member names say.
    [Theory]
    [InlineData(GenreCategory.LickLibraryLtp, "Erotica")]
    [InlineData(GenreCategory.SheetCollectionMp3, "Accompanying Music")]
    [InlineData(GenreCategory.Military, "Military")]
    [InlineData(GenreCategory.ScienceFiction, "Science Fiction")]
    public void DisplayName_ShortName_AsWrittenToMetadataFiles(GenreCategory category, string expected) =>
        Assert.Equal(expected, GenreCategoryDisplay.DisplayName(category));

    [Fact]
    public void DisplayName_UnknownId_ShowsUnknownCategoryWithId()
    {
        Assert.Equal("Unknown Category (id: 64)", GenreCategoryDisplay.DisplayName((GenreCategory)64));
    }
}
