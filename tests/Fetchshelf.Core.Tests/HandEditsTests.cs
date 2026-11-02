using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class HandEditsTests
{
    private static TorrentMetadata Metadata(string title, MetadataSource source, VipStatus? vip = null) => new()
    {
        MamId = 1,
        VipStatus = vip ?? VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        Language = Language.English,
        Title = title,
        Authors = ["Frank Herbert"],
        MetadataSource = source,
        UploadTime = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void KeepOver_HandEditedStored_KeepsTheEditsAndTakesOnlyVipStatus()
    {
        var kept = HandEdits.KeepOver(Metadata("My Title", MetadataSource.Manual), Metadata("MAM Title", MetadataSource.Mam, VipStatus.Permanent));

        Assert.Equal("My Title", kept.Title);
        Assert.Equal(MetadataSource.Manual, kept.MetadataSource);
        Assert.Equal(VipStatus.Permanent, kept.VipStatus);
    }

    [Fact]
    public void KeepOver_StoredFromMam_TakesTheFreshDetails()
    {
        var fresh = Metadata("MAM Title", MetadataSource.Mam);

        Assert.Same(fresh, HandEdits.KeepOver(Metadata("Old Title", MetadataSource.Mam), fresh));
    }
}
