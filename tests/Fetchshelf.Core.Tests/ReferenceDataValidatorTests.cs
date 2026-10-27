using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Tests;

public class ReferenceDataValidatorTests
{
    private static TorrentMetadata Build(
        MediaType mediaType = MediaType.Audiobook,
        LegacyCategory legacyCategory = LegacyCategory.AudiobookGeneralFiction,
        Language language = Language.English,
        long totalSizeBytes = 100,
        DateTimeOffset? uploadTime = null,
        IReadOnlySet<GenreCategory>? genreCategories = null) => new()
        {
            MamId = 1,
            VipStatus = VipStatus.NotVip,
            MediaType = mediaType,
            LegacyCategory = legacyCategory,
            Language = language,
            GenreCategories = genreCategories ?? new HashSet<GenreCategory>(),
            TotalSizeBytes = totalSizeBytes,
            Title = "Some Title",
            Authors = ["Jane Doe"],
            MetadataSource = MetadataSource.Mam,
            UploadTime = uploadTime ?? DateTimeOffset.UtcNow,
        };

    [Fact]
    public void EnsureRecognized_AllFieldsValid_DoesNotThrow()
    {
        var metadata = Build(genreCategories: new HashSet<GenreCategory> { GenreCategory.Fantasy });

        ReferenceDataValidator.EnsureRecognized(metadata);
    }

    [Fact]
    public void EnsureRecognized_UnknownGenreCategory_DoesNotThrow()
    {
        var metadata = Build(genreCategories: new HashSet<GenreCategory> { (GenreCategory)63 });

        ReferenceDataValidator.EnsureRecognized(metadata);
    }

    [Fact]
    public void EnsureRecognized_UnknownMediaType_ThrowsWithDescriptiveMessage()
    {
        var metadata = Build(mediaType: (MediaType)999);

        var exception = Assert.Throws<UnrecognizedReferenceDataException>(() => ReferenceDataValidator.EnsureRecognized(metadata));
        Assert.Equal("media type", exception.FieldName);
        Assert.Contains("999", exception.Message);
    }

    [Fact]
    public void EnsureRecognized_UnknownLegacyCategory_Throws()
    {
        var metadata = Build(legacyCategory: (LegacyCategory)999999);

        Assert.Throws<UnrecognizedReferenceDataException>(() => ReferenceDataValidator.EnsureRecognized(metadata));
    }

    [Fact]
    public void EnsureRecognized_UnknownLanguage_Throws()
    {
        var metadata = Build(language: (Language)999);

        Assert.Throws<UnrecognizedReferenceDataException>(() => ReferenceDataValidator.EnsureRecognized(metadata));
    }

    [Fact]
    public void EnsureRecognized_NegativeSize_Throws()
    {
        var metadata = Build(totalSizeBytes: -1);

        Assert.Throws<UnrecognizedReferenceDataException>(() => ReferenceDataValidator.EnsureRecognized(metadata));
    }

    [Fact]
    public void EnsureRecognized_DefaultUploadTime_Throws()
    {
        var metadata = Build(uploadTime: default(DateTimeOffset));

        Assert.Throws<UnrecognizedReferenceDataException>(() => ReferenceDataValidator.EnsureRecognized(metadata));
    }
}
