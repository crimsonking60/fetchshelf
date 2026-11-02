using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class LegacyCategoryGroupsTests
{
    [Theory]
    [InlineData(LegacyCategory.AudiobookFantasy, MediaType.Audiobook)]
    [InlineData(LegacyCategory.EbookFantasy, MediaType.Ebook)]
    [InlineData(LegacyCategory.EbookComicsGraphicNovels, MediaType.Ebook)]
    [InlineData(LegacyCategory.MusicologySheetCollection, MediaType.Musicology)]
    [InlineData(LegacyCategory.RadioDrama, MediaType.Radio)]
    public void MediaTypeOf_Category_ReturnsItsGroupsMediaType(LegacyCategory category, MediaType expected) =>
        Assert.Equal(expected, LegacyCategoryGroups.MediaTypeOf(category));

    [Fact]
    public void MediaTypeOf_EveryCategory_BelongsToExactlyOneGroup()
    {
        foreach (var category in Enum.GetValues<LegacyCategory>())
        {
            var groups = new[] { LegacyCategoryGroups.Audiobook, LegacyCategoryGroups.Ebook, LegacyCategoryGroups.Musicology, LegacyCategoryGroups.Radio };
            Assert.Equal(1, groups.Count(g => g.Contains(category)));
        }
    }
}
