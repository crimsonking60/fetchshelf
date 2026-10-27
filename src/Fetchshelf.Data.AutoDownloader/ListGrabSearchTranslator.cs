using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Translates a list import's title+author query and a `grab` block's shared filter into a
/// `MamSearchQuery`, Fetchshelf-spec.md §7.8 step 3. Reuses <see cref="AutogrebSearchTranslator"/>'s
/// category-id translation rather than duplicating it.
/// </summary>
public static class ListGrabSearchTranslator
{
    public static MamSearchQuery ToSearchQuery(TorrentFilter filter, string queryText, long pageOffset) => new()
    {
        Text = queryText,
        SearchFields = [MamSearchField.Title, MamSearchField.Author],
        LanguageIds = filter.Languages?.Select(l => (int)l).ToList() ?? [],
        MainCategoryIds = AutogrebSearchTranslator.ToMainCategoryIds(filter.Categories),
        CategoryIds = AutogrebSearchTranslator.ToCategoryIds(filter.Categories),
        StartDate = filter.UploadedAfter,
        EndDate = filter.UploadedBefore,
        MinSizeBytes = filter.MinSizeBytes,
        MaxSizeBytes = filter.MaxSizeBytes,
        MinSeeders = filter.MinSeeders,
        MaxSeeders = filter.MaxSeeders,
        MinLeechers = filter.MinLeechers,
        MaxLeechers = filter.MaxLeechers,
        MinSnatched = filter.MinSnatched,
        MaxSnatched = filter.MaxSnatched,
        ShowFlags = AutogrebSearchTranslator.ToShowFlags(filter.Flags),
        Flags = filter.Flags?.Keys.Select(f => (int)f).ToList() ?? [],
        PageOffset = pageOffset,
        PerPage = 100,
        ReturnDownloadLink = true,
    };

    /// <summary>
    /// Returns <paramref name="filter"/> with a side's category group zeroed out when that side no
    /// longer needs searching, mirroring the reference's "categories.audio = Some(vec![])" once a
    /// side is already found (Fetchshelf-spec.md §7.8 step 3).
    /// </summary>
    public static TorrentFilter NarrowToNeededSides(TorrentFilter filter, bool needAudio, bool needEbook) => filter with
    {
        Categories = filter.Categories with
        {
            Audio = needAudio ? filter.Categories.Audio : MainTypeCategoryFilter.None,
            Ebook = needEbook ? filter.Categories.Ebook : MainTypeCategoryFilter.None,
        },
    };
}
