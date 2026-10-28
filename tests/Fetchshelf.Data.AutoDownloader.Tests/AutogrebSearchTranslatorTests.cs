using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.AutoDownloader.Tests;

public class AutogrebSearchTranslatorTests
{
    private static AutogrebEntry Entry(AutogrebType type, string? sortBy = null) => new()
    {
        Type = type,
    };

    [Fact]
    public void SearchIn_MapsToSearchFields()
    {
        var entry = Entry(AutogrebType.New);
        entry.SearchIn = ["author", "file_types", "Title"];

        var query = AutogrebSearchTranslator.ToSearchQuery(entry, new TorrentFilter(), 0);

        Assert.Equal([MamSearchField.Author, MamSearchField.FileTypes, MamSearchField.Title], query.SearchFields);
    }

    [Fact]
    public void SearchIn_Unset_LeavesSearchFieldsEmpty()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.New), new TorrentFilter(), 0);

        Assert.Empty(query.SearchFields);
    }

    [Fact]
    public void SearchIn_UnknownField_Throws()
    {
        var entry = Entry(AutogrebType.New);
        entry.SearchIn = ["isbn"];

        Assert.Throws<ArgumentOutOfRangeException>(() => AutogrebSearchTranslator.ToSearchQuery(entry, new TorrentFilter(), 0));
    }

    [Fact]
    public void BookmarksType_MapsToBookmarksTarget()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Bookmarks), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.Bookmarks, query.Target);
        Assert.Null(query.Kind);
    }

    [Fact]
    public void FreeleechType_MapsToNewTargetWithFreeleechKind()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Freeleech), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.New, query.Target);
        Assert.Equal(MamSearchKind.Freeleech, query.Kind);
    }

    [Fact]
    public void UploaderType_MapsToUploaderTarget()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Uploader(42)), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.Uploader(42), query.Target);
    }

    [Fact]
    public void NewType_MapsToNewTarget()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.New), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.New, query.Target);
    }

    [Fact]
    public void MineType_MapsToMineTarget()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Mine), new TorrentFilter(), 0);

        Assert.Equal(MamSearchTarget.Mine, query.Target);
    }

    [Fact]
    public void EverySharedFilterField_ReachesTheQuery()
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

        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Bookmarks), filter, pageOffset: 100);

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
    public void AllCategoriesAllowed_ProducesNoCategoryFilter()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Bookmarks), new TorrentFilter
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
    public void DefaultCategories_UsesMainCategoryIdsForAudioAndEbook()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Bookmarks), new TorrentFilter(), 0);

        Assert.Equal([13, 14], query.MainCategoryIds);
        Assert.Empty(query.CategoryIds);
    }

    [Fact]
    public void ExplicitCategoryList_ExpandsAllOtherTypesToEverySubId()
    {
        var query = AutogrebSearchTranslator.ToSearchQuery(Entry(AutogrebType.Bookmarks), new TorrentFilter
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
