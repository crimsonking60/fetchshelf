using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class TorrentMetadataSerializerTests
{
    [Fact]
    public void SerializeThenDeserialize_FullMetadata_RoundTripsUnchanged()
    {
        var metadata = new TorrentMetadata
        {
            MamId = 42,
            VipStatus = VipStatus.Permanent,
            MediaType = MediaType.Audiobook,
            LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
            GenreCategories = new HashSet<GenreCategory> { GenreCategory.Fantasy, GenreCategory.Action },
            Language = Language.English,
            Flags = BookFlags.Lgbt | BookFlags.Abridged,
            FileTypes = ["m4b", "epub"],
            FileCount = 2,
            TotalSizeBytes = 123456,
            Title = "The Way of Kings: A Novel",
            Edition = new Edition("2nd Edition", 2),
            Authors = ["Brandon Sanderson"],
            Narrators = ["Michael Kramer"],
            Series = [new Series { Name = "Stormlight Archive", Entries = [SeriesEntry.ForNumber(1, "1")] }],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Description = "An epic fantasy novel.",
            Isbn = "9780765326355",
            Asin = null,
        };

        var json = TorrentMetadataSerializer.Serialize(metadata);
        var roundTripped = TorrentMetadataSerializer.Deserialize(json);

        Assert.Equal(metadata.MamId, roundTripped.MamId);
        Assert.Equal(metadata.Title, roundTripped.Title);
        Assert.Equal(metadata.Authors, roundTripped.Authors);
        Assert.Equal(metadata.GenreCategories, roundTripped.GenreCategories);
        Assert.Equal(metadata.Series.Count, roundTripped.Series.Count);
        Assert.Equal(metadata.Series[0].Name, roundTripped.Series[0].Name);
        Assert.Equal(metadata.Description, roundTripped.Description);
        Assert.Equal(metadata.Isbn, roundTripped.Isbn);
        Assert.Equal(metadata.Flags, roundTripped.Flags);
    }

    [Fact]
    public void SerializeThenDeserialize_UnknownGenreCategoryId_IsPreserved()
    {
        var metadata = new TorrentMetadata
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = MediaType.Ebook,
            LegacyCategory = LegacyCategory.EbookGeneralFiction,
            GenreCategories = new HashSet<GenreCategory> { GenreCategory.Fantasy, (GenreCategory)63 },
            Language = Language.English,
            Title = "T",
            Authors = ["A"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        };

        var roundTripped = TorrentMetadataSerializer.Deserialize(TorrentMetadataSerializer.Serialize(metadata));

        Assert.Contains((GenreCategory)63, roundTripped.GenreCategories);
        Assert.Contains(GenreCategory.Fantasy, roundTripped.GenreCategories);
    }
}
