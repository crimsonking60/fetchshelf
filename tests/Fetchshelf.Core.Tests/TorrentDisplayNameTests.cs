using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class TorrentDisplayNameTests
{
    private static TorrentMetadata With(params string[] authors) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Ebook,
        LegacyCategory = LegacyCategory.EbookFantasy,
        Language = Language.English,
        Title = "The Way of Kings",
        Authors = authors,
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void DisplayName_NoAuthors_IsTheTitle() => Assert.Equal("The Way of Kings", With().DisplayName());

    [Fact]
    public void DisplayName_OneAuthor_NamesThem() =>
        Assert.Equal("The Way of Kings by Brandon Sanderson", With("Brandon Sanderson").DisplayName());

    [Fact]
    public void DisplayName_TwoAuthors_NamesBoth() => Assert.Equal("The Way of Kings by A, B", With("A", "B").DisplayName());

    [Fact]
    public void DisplayName_ManyAuthors_NamesTwoAndCountsTheRest()
    {
        Assert.Equal("The Way of Kings by A, B and 1 other", With("A", "B", "C").DisplayName());
        Assert.Equal("The Way of Kings by A, B and 2 others", With("A", "B", "C", "D").DisplayName());
    }
}
