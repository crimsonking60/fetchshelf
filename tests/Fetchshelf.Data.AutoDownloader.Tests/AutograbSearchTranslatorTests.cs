using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public class AutograbSearchTranslatorTests
{
    private static AutograbEntry Entry(AutograbType type, string? sortBy = null) => new()
    {
        Type = type,
    };

    [Theory]
    [InlineData(null, MamSortOrder.NewestFirst)]
    [InlineData("newest_first", MamSortOrder.NewestFirst)]
    [InlineData("oldest_first", MamSortOrder.OldestFirst)]
    [InlineData("random", MamSortOrder.Random)]
    [InlineData("low_seeders", MamSortOrder.LowestSeeders)]
    [InlineData("low_snatches", MamSortOrder.LowestSnatches)]
    public void ToSearchQuery_SortBy_MapsToSortOrder(string? sortBy, MamSortOrder expected)
    {
        var entry = Entry(AutograbType.New);
        entry.SortBy = sortBy;

        var query = AutograbSearchTranslator.ToSearchQuery(entry, new TorrentFilter(), 0);

        Assert.Equal(expected, query.SortOrder);
    }

    [Fact]
    public void ToSearchQuery_SearchIn_MapsToSearchFields()
    {
        var entry = Entry(AutograbType.New);
        entry.SearchIn = ["author", "file_types", "Title"];

        var query = AutograbSearchTranslator.ToSearchQuery(entry, new TorrentFilter(), 0);

        Assert.Equal([MamSearchField.Author, MamSearchField.FileTypes, MamSearchField.Title], query.SearchFields);
    }

    [Fact]
    public void SearchIn_Unset_LeavesSearchFieldsEmpty()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.New), new TorrentFilter(), 0);

        Assert.Empty(query.SearchFields);
    }

    [Fact]
    public void SearchIn_UnknownField_Throws()
    {
        var entry = Entry(AutograbType.New);
        entry.SearchIn = ["isbn"];

        Assert.Throws<ArgumentOutOfRangeException>(() => AutograbSearchTranslator.ToSearchQuery(entry, new TorrentFilter(), 0));
    }

    [Fact]
    public void ToSearchQuery_BookmarksType_MapsToBookmarksTarget()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Bookmarks), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.Bookmarks, query.Target);
        Assert.Null(query.Kind);
    }

    [Fact]
    public void ToSearchQuery_FreeleechType_MapsToNewTargetWithFreeleechKind()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Freeleech), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.New, query.Target);
        Assert.Equal(MamSearchKind.Freeleech, query.Kind);
    }

    [Fact]
    public void ToSearchQuery_UploaderType_MapsToUploaderTarget()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Uploader(42)), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.Uploader(42), query.Target);
    }

    [Fact]
    public void ToSearchQuery_NewType_MapsToNewTarget()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.New), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.New, query.Target);
    }

    [Fact]
    public void ToSearchQuery_MineType_MapsToMineTarget()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Mine), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.Mine, query.Target);
    }

    [Fact]
    public void ToSearchQuery_EverySharedFilterField_ReachesTheQuery()
    {
        var filter = new TorrentFilter
        {
            Languages = [Language.English],
            UploadedAfter = new DateOnly(2026, 1, 1),
            UploadedBefore = new DateOnly(2026, 12, 31),
            MinSizeBytes = 100,
            MaxSizeBytes = 200,
            MinSeeders = 1,
            MaxSeeders = 10,
            MinLeechers = 2,
            MaxLeechers = 20,
            MinSnatched = 3,
            MaxSnatched = 30,
            Flags = new Dictionary<BookFlags, bool> { [BookFlags.Explicit] = true },
        };

        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Bookmarks), filter, pageOffset: 100);

        Assert.Equal([(int)Language.English], query.LanguageIds);
        Assert.Equal(filter.UploadedAfter, query.StartDate);
        Assert.Equal(filter.UploadedBefore, query.EndDate);
        Assert.Equal(100, query.MinSizeBytes);
        Assert.Equal(200, query.MaxSizeBytes);
        Assert.Equal(1, query.MinSeeders);
        Assert.Equal(10, query.MaxSeeders);
        Assert.Equal(2, query.MinLeechers);
        Assert.Equal(20, query.MaxLeechers);
        Assert.Equal(3, query.MinSnatched);
        Assert.Equal(30, query.MaxSnatched);
        Assert.Equal([(int)BookFlags.Explicit], query.Flags);
        Assert.True(query.ShowFlags);
        Assert.Equal(100, query.PageOffset);
    }

    [Fact]
    public void ToSearchQuery_AllCategoriesAllowed_ProducesNoCategoryFilter()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Bookmarks), new TorrentFilter
        {
            Categories = new CategoryFilter
            {
                Audio = MainTypeCategoryFilter.All,
                Ebook = MainTypeCategoryFilter.All,
                Musicology = MainTypeCategoryFilter.All,
                Radio = MainTypeCategoryFilter.All,
            },
        }, 0);

        Assert.Empty(query.MainCategoryIds);
        Assert.Empty(query.CategoryIds);
    }

    [Fact]
    public void ToSearchQuery_DefaultCategories_UsesMainCategoryIdsForAudioAndEbook()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Bookmarks), new TorrentFilter(), 0);

        Assert.Equal([13, 14], query.MainCategoryIds);
        Assert.Empty(query.CategoryIds);
    }

    [Fact]
    public void ToSearchQuery_ExplicitCategoryList_ExpandsAllOtherTypesToEverySubId()
    {
        var query = AutograbSearchTranslator.ToSearchQuery(Entry(AutograbType.Bookmarks), new TorrentFilter
        {
            Categories = new CategoryFilter
            {
                Audio = new MainTypeCategoryFilter { Allowed = new HashSet<LegacyCategory> { LegacyCategory.AudiobookFantasy } },
            },
        }, 0);

        Assert.Empty(query.MainCategoryIds);
        Assert.Contains((int)LegacyCategory.AudiobookFantasy, query.CategoryIds);
        // Ebook is still "all" (default), so with an explicit list present elsewhere, every ebook id is expanded.
        Assert.Contains((int)LegacyCategory.EbookFantasy, query.CategoryIds);
        // Musicology/Radio default to "none" and contribute nothing.
        Assert.DoesNotContain((int)LegacyCategory.MusicologySheetCollection, query.CategoryIds);
    }
}
