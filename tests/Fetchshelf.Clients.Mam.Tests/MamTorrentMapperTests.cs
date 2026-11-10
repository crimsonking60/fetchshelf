using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamTorrentMapperTests
{
    private static MamTorrentRecord BuildRecord(
        int mediaTypeId = 1,
        int oldMainCategoryId = 13,
        int fictionCategoryId = 1,
        int legacyCategoryId = (int)LegacyCategory.AudiobookActionAdventure,
        IReadOnlyList<int>? genreCategoryIds = null,
        int languageId = 1,
        int flagsRaw = 0,
        string title = "Dune",
        IReadOnlyDictionary<long, string>? authors = null,
        string sizeRaw = "1.23 GiB",
        string tagsRaw = "sci-fi") => new()
        {
            MamId = 123456,
            Title = title,
            Authors = authors ?? new Dictionary<long, string> { [1] = "Frank Herbert" },
            Narrators = new Dictionary<long, string> { [2] = "Simon Vance" },
            Series = [new MamSeriesInfo("Dune", "1")],
            Isbn = "9780441013593",
            Description = "A desert planet.",
            MediaTypeId = mediaTypeId,
            OldMainCategoryId = oldMainCategoryId,
            FictionCategoryId = fictionCategoryId,
            LegacyCategoryId = legacyCategoryId,
            GenreCategoryIds = genreCategoryIds ?? [GenreCategoryIdFor(GenreCategory.ScienceFiction)],
            LanguageId = languageId,
            FlagsRaw = flagsRaw,
            FileTypesRaw = "m4b",
            FileCount = 12,
            SizeRaw = sizeRaw,
            TagsRaw = tagsRaw,
            UploaderId = 999,
            UploaderName = "uploader1",
            UploadedAt = new DateTimeOffset(2020, 5, 1, 12, 34, 56, TimeSpan.Zero),
            Seeders = 10,
            Leechers = 2,
            TimesCompleted = 50,
            IsFree = true,
            IsPersonalFreeleech = false,
            IsFreeleechVip = false,
            IsVip = false,
            VipExpireUnixSeconds = 0,
            DownloadHash = "abcdef0123456789",
        };

    private static int GenreCategoryIdFor(GenreCategory category) => (int)category;

    [Fact]
    public void Map_CleanRecord_ProducesCorrectlyMappedAndCleanedMetadata()
    {
        var record = BuildRecord(title: "Dune 2nd Edition");

        var metadata = MamTorrentMapper.Map(record);

        Assert.Equal(123456, metadata.MamId);
        Assert.Equal("Dune", metadata.Title);
        Assert.Equal(2, metadata.Edition!.Number);
        Assert.Equal(MediaType.Audiobook, metadata.MediaType);
        Assert.Equal(FictionCategory.Fiction, metadata.FictionCategory);
        Assert.Equal(["Frank Herbert"], metadata.Authors);
        Assert.Equal(["Simon Vance"], metadata.Narrators);
        Assert.Equal("Dune", metadata.Series.Single().Name);
        Assert.Equal("9780441013593", metadata.Isbn);
        Assert.Null(metadata.Asin);
        Assert.Equal("A desert planet.", metadata.Description);
        Assert.Contains(GenreCategory.ScienceFiction, metadata.GenreCategories);
        Assert.Equal(VipStatus.NotVip, metadata.VipStatus);
        Assert.Equal(MetadataSource.Mam, metadata.MetadataSource);
    }

    [Fact]
    public void Map_NoAuthorsButAuthorInTitle_TakesTheAuthorFromTheTitle()
    {
        var record = BuildRecord(title: "Exploitation Film Posters by GRAHAM MARSH", authors: new Dictionary<long, string>());

        var metadata = MamTorrentMapper.Map(record);

        Assert.Equal("Exploitation Film Posters", metadata.Title);
        Assert.Equal(["Graham Marsh"], metadata.Authors);
    }

    [Fact]
    public void Map_AuthorsGiven_LeavesANameInTheTitleToTheUsualStripping()
    {
        var record = BuildRecord(title: "Dune by Brian Herbert");

        var metadata = MamTorrentMapper.Map(record);

        Assert.Equal("Dune by Brian Herbert", metadata.Title);
        Assert.Equal(["Frank Herbert"], metadata.Authors);
    }

    [Fact]
    public void Map_PeriodicalWithoutAuthors_KeepsItsTitle()
    {
        var record = BuildRecord(
            mediaTypeId: (int)MediaType.PeriodicalEbook, title: "Film Monthly by Picture House", authors: new Dictionary<long, string>());

        var metadata = MamTorrentMapper.Map(record);

        Assert.Equal("Film Monthly by Picture House", metadata.Title);
        Assert.Empty(metadata.Authors);
    }

    [Fact]
    public void Map_AsinPrefixedIdentifier_SplitsIntoAsinField()
    {
        var record = BuildRecord() with { Isbn = "ASIN:B000FC1PWU" };

        var metadata = MamTorrentMapper.Map(record);

        Assert.Null(metadata.Isbn);
        Assert.Equal("B000FC1PWU", metadata.Asin);
    }

    [Fact]
    public void Map_UnrecognizedLanguage_ThrowsUnrecognizedReferenceDataException()
    {
        var record = BuildRecord(languageId: 999);

        Assert.Throws<UnrecognizedReferenceDataException>(() => MamTorrentMapper.Map(record));
    }

    [Fact]
    public void Map_OldRecordWithOnlyMainCategory_FallsBackToOldMainCategory()
    {
        var record = BuildRecord(mediaTypeId: 0, oldMainCategoryId: 14, legacyCategoryId: (int)LegacyCategory.EbookActionAdventure);

        var metadata = MamTorrentMapper.Map(record);

        Assert.Equal(MediaType.Ebook, metadata.MediaType);
    }
}
