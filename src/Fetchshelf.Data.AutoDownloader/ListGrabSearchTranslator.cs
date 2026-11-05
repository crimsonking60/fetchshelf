using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Turns a list import's title and author search text and a `grab` block's shared filter into a
/// `MamSearchQuery`, reusing <see cref="AutograbSearchTranslator"/>'s category translation.
/// </summary>
public static class ListGrabSearchTranslator
{
    public static MamSearchQuery ToSearchQuery(TorrentFilter filter, string queryText, long pageOffset) => new()
    {
        Text = queryText,
        SearchFields = [MamSearchField.Title, MamSearchField.Author],
        LanguageIds = filter.Languages?.Select(l => (int)l).ToList() ?? [],
        MainCategoryIds = AutograbSearchTranslator.ToMainCategoryIds(filter.Categories),
        CategoryIds = AutograbSearchTranslator.ToCategoryIds(filter.Categories),
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
        ShowFlags = AutograbSearchTranslator.ToShowFlags(filter.Flags),
        Flags = filter.Flags?.Keys.Select(f => (int)f).ToList() ?? [],
        PageOffset = pageOffset,
        PerPage = 100,
        ReturnDownloadLink = true,
    };

    /// <summary>
    /// Returns <paramref name="filter"/> with a side's categories emptied when that side no longer needs searching,
    /// so a book already found as an audiobook is only searched for as an ebook, and the other way round.
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
