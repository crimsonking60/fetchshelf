using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class MetadataDiffTests
{
    private static readonly TorrentMetadata Original = new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        Language = Language.English,
        Title = "Dune",
        Authors = ["Frank Herbert"],
        Narrators = ["Scott Brick"],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Fields_AreEveryFieldBetweenRecords_InItsOrder() =>
        Assert.Equal(
            MetadataDiff.Fields,
            MetadataDiff.Between(Original, Original with
            {
                Title = "Dune Messiah",
                Authors = ["Brian Herbert"],
                Narrators = ["Euan Morton"],
                Language = Language.German,
                LegacyCategory = LegacyCategory.AudiobookScienceFiction,
            }).Select(change => change.Field));

    [Fact]
    public void Between_SameMetadata_IsEmpty() =>
        Assert.Empty(MetadataDiff.Between(Original, Original with { }));

    [Fact]
    public void Between_TitleAndAuthorsChanged_ListsBothInFieldOrder() =>
        Assert.Equal(
            [new FieldChange("Title", "Dune", "Dune Messiah"), new FieldChange("Authors", "Frank Herbert", "Frank Herbert, Brian Herbert")],
            MetadataDiff.Between(Original, Original with { Title = "Dune Messiah", Authors = ["Frank Herbert", "Brian Herbert"] }));

    [Fact]
    public void Between_OnlyAnUnrecordedFieldChanged_IsEmpty() =>
        Assert.Empty(MetadataDiff.Between(Original, Original with { VipStatus = VipStatus.Permanent }));
}
