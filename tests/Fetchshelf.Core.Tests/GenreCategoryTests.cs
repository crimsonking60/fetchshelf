using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class GenreCategoryTests
{
    // Real tracker ids, confirmed against the original Rust implementation's reference data
    // (mlm_db::impls::categories::Category::from_id/as_id).
    [Theory]
    [InlineData(GenreCategory.Action, 1)]
    [InlineData(GenreCategory.SheetCollection, 47)]
    [InlineData(GenreCategory.SheetCollectionMp3, 48)]
    [InlineData(GenreCategory.Sports, 49)]
    [InlineData(GenreCategory.DramaPlays, 60)]
    [InlineData(GenreCategory.Occult, 61)]
    [InlineData(GenreCategory.SliceOfLife, 62)]
    public void NumericValue_MatchesRealTrackerId(GenreCategory category, int expectedId)
    {
        Assert.Equal(expectedId, (int)category);
    }

    [Fact]
    public void DisplayName_UnknownId_ShowsUnknownCategoryWithId()
    {
        Assert.Equal("Unknown Category (id: 63)", GenreCategoryDisplay.DisplayName((GenreCategory)63));
    }
}
