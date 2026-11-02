using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader.Tests;

public class TorrentFilterMatcherTests
{
    private static TorrentFilterCandidate Build(
        MediaType mediaType = MediaType.Audiobook,
        LegacyCategory category = LegacyCategory.AudiobookGeneralFiction,
        Language language = Language.English,
        BookFlags flags = BookFlags.None,
        long sizeBytes = 100,
        string uploaderName = "uploader",
        DateTimeOffset? uploadTime = null,
        long seeders = 5,
        long leechers = 1,
        long timesCompleted = 10,
        int authorCount = 1) => new()
        {
            Metadata = new TorrentMetadata
            {
                MamId = 1,
                VipStatus = VipStatus.NotVip,
                MediaType = mediaType,
                LegacyCategory = category,
                Language = language,
                Flags = flags,
                TotalSizeBytes = sizeBytes,
                Title = "Some Title",
                Authors = Enumerable.Range(1, authorCount).Select(i => $"Author {i}").ToList(),
                MetadataSource = MetadataSource.Mam,
                UploadTime = uploadTime ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            UploaderName = uploaderName,
            Seeders = seeders,
            Leechers = leechers,
            TimesCompleted = timesCompleted,
        };

    [Fact]
    public void Matches_UnsetFields_ImposeNothing()
    {
        var filter = new TorrentFilter { MediaTypes = [MediaType.Audiobook] };
        var candidate = Build(mediaType: MediaType.Audiobook, sizeBytes: 999_999, seeders: 0);

        Assert.True(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void MediaType_Mismatch_Fails()
    {
        var filter = new TorrentFilter { MediaTypes = [MediaType.Ebook] };
        var candidate = Build(mediaType: MediaType.Audiobook);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void Languages_Mismatch_Fails()
    {
        var filter = new TorrentFilter { Languages = [Language.English] };
        var candidate = Build(language: Language.French);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void Matches_DefaultCategories_ExcludesMusicologyAndRadio()
    {
        var filter = new TorrentFilter();
        var candidate = Build(mediaType: MediaType.Musicology, category: LegacyCategory.MusicologySheetCollection);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void Matches_DefaultCategories_IncludesAllAudioAndEbook()
    {
        var filter = new TorrentFilter();
        var candidate = Build(mediaType: MediaType.Audiobook, category: LegacyCategory.AudiobookHorror);

        Assert.True(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void Matches_ExplicitCategoryList_RestrictsToThoseCategories()
    {
        var filter = new TorrentFilter
        {
            Categories = new CategoryFilter
            {
                Audio = new MainTypeCategoryFilter { Allowed = new HashSet<LegacyCategory> { LegacyCategory.AudiobookFantasy } },
            },
        };
        var candidate = Build(mediaType: MediaType.Audiobook, category: LegacyCategory.AudiobookHorror);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void Flags_RequiredFlagMissing_Fails()
    {
        var filter = new TorrentFilter { Flags = new Dictionary<BookFlags, bool> { [BookFlags.Explicit] = true } };
        var candidate = Build(flags: BookFlags.None);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void Flags_ForbiddenFlagPresent_Fails()
    {
        var filter = new TorrentFilter { Flags = new Dictionary<BookFlags, bool> { [BookFlags.Explicit] = false } };
        var candidate = Build(flags: BookFlags.Explicit);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Theory]
    [InlineData(50, 100, 200, false)]
    [InlineData(150, 100, 200, true)]
    [InlineData(250, 100, 200, false)]
    public void Matches_SizeAroundRange_IncludesTheEdges(long sizeBytes, long min, long max, bool expectedMatch)
    {
        var filter = new TorrentFilter { MinSizeBytes = min, MaxSizeBytes = max };
        var candidate = Build(sizeBytes: sizeBytes);

        Assert.Equal(expectedMatch, TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(12, false)]
    public void Matches_AuthorCountAroundMaxAuthors_IncludesTheMaximum(int authorCount, bool expectedMatch)
    {
        var filter = new TorrentFilter { MaxAuthors = 3 };

        Assert.Equal(expectedMatch, TorrentFilterMatcher.Matches(filter, Build(authorCount: authorCount)));
    }

    [Fact]
    public void MaxAuthors_Unset_AllowsAnyAuthorCount()
    {
        Assert.True(TorrentFilterMatcher.Matches(new TorrentFilter(), Build(authorCount: 50)));
    }

    [Fact]
    public void ExcludeUploader_MatchingUploader_Fails()
    {
        var filter = new TorrentFilter { ExcludeUploader = ["bad-uploader"] };
        var candidate = Build(uploaderName: "bad-uploader");

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void UploadDateRange_OutsideRange_Fails()
    {
        var filter = new TorrentFilter
        {
            UploadedAfter = new DateOnly(2026, 2, 1),
            UploadedBefore = new DateOnly(2026, 3, 1),
        };
        var candidate = Build(uploadTime: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Theory]
    [InlineData(5, 10, 20, false)]
    [InlineData(15, 10, 20, true)]
    public void Matches_SeedersAroundRange_IncludesTheEdges(long seeders, long min, long max, bool expectedMatch)
    {
        var filter = new TorrentFilter { MinSeeders = min, MaxSeeders = max };
        var candidate = Build(seeders: seeders);

        Assert.Equal(expectedMatch, TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void LeecherRange_OutsideRange_Fails()
    {
        var filter = new TorrentFilter { MaxLeechers = 5 };
        var candidate = Build(leechers: 10);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }

    [Fact]
    public void SnatchedRange_OutsideRange_Fails()
    {
        var filter = new TorrentFilter { MinSnatched = 20 };
        var candidate = Build(timesCompleted: 5);

        Assert.False(TorrentFilterMatcher.Matches(filter, candidate));
    }
}
