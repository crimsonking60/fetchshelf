using Fetchshelf.Clients.Mam;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Data.Configuration;

namespace Fetchshelf.Data.AutoDownloader;

/// <summary>
/// Translates an `[[autograb]]`/`[[snatchlist]]` entry's `type`/`sort_by`/shared filter into a
/// `MamSearchQuery`. The server-side translation is a best-effort narrowing
/// of what MaM returns - the full shared filter is always re-applied locally afterward (see
/// `AutograbberRunner`), so an imperfect translation here cannot produce an incorrect selection.
/// </summary>
public static class AutogrebSearchTranslator
{
    // Old main-category ids
    private const int AudioMainCategoryId = 13;
    private const int EbookMainCategoryId = 14;
    private const int MusicologyMainCategoryId = 15;
    private const int RadioMainCategoryId = 16;

    public static MamSearchQuery ToSearchQuery(AutogrebEntry entry, TorrentFilter filter, long pageOffset)
    {
        var (target, kind) = ToTargetAndKind(entry.Type!);

        return new MamSearchQuery
        {
            Text = entry.Query,
            Target = target,
            Kind = kind,
            SortOrder = ToSortOrder(entry.SortBy),
            LanguageIds = filter.Languages?.Select(l => (int)l).ToList() ?? [],
            MainCategoryIds = ToMainCategoryIds(filter.Categories),
            CategoryIds = ToCategoryIds(filter.Categories),
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
            ShowFlags = ToShowFlags(filter.Flags),
            Flags = filter.Flags?.Keys.Select(f => (int)f).ToList() ?? [],
            PageOffset = pageOffset,
            PerPage = 100,
            ReturnDownloadLink = true,
        };
    }

    private static (MamSearchTarget Target, MamSearchKind? Kind) ToTargetAndKind(AutogrebType type) => type.Kind switch
    {
        "bookmarks" => (MamSearchTarget.Bookmarks, null),
        "new" => (MamSearchTarget.New, null),
        "mine" => (MamSearchTarget.Mine, null),
        "uploader" => (MamSearchTarget.Uploader(type.UploaderId!.Value), null),
        // "freeleech" means the global freeleech list layered on top of the general torrent list -
        // there is no dedicated freeleech search target
        "freeleech" => (MamSearchTarget.New, MamSearchKind.Freeleech),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static MamSortOrder ToSortOrder(string? sortBy) => sortBy?.ToLowerInvariant() switch
    {
        null => MamSortOrder.NewestFirst,
        "oldest_first" => MamSortOrder.OldestFirst,
        "random" => MamSortOrder.Random,
        "low_seeders" => MamSortOrder.LowestSeeders,
        "low_snatches" => MamSortOrder.LowestSnatches,
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy)),
    };

    internal static bool? ToShowFlags(IReadOnlyDictionary<BookFlags, bool>? flags)
    {
        if (flags is null || flags.Count == 0)
        {
            return null;
        }

        var showCount = flags.Values.Count(v => v);
        var hideCount = flags.Values.Count(v => !v);
        return hideCount <= showCount;
    }

    /// <summary>
    /// Mirrors the reference implementation's `Categories::get_main_cats` (Fetchshelf-spec.md §4.3/§12.1):
    /// used only when no main type has an explicit non-empty category list (otherwise specific
    /// category ids carry the filter instead, see <see cref="ToCategoryIds"/>), and only for the
    /// main types set to "all" - unless every type is "all", in which case no filter is needed.
    /// </summary>
    internal static List<int> ToMainCategoryIds(CategoryFilter categories)
    {
        if (HasAnyExplicitList(categories))
        {
            return [];
        }

        var mainCats = new List<int>();
        if (categories.Audio.Allowed is null)
        {
            mainCats.Add(AudioMainCategoryId);
        }

        if (categories.Ebook.Allowed is null)
        {
            mainCats.Add(EbookMainCategoryId);
        }

        if (categories.Musicology.Allowed is null)
        {
            mainCats.Add(MusicologyMainCategoryId);
        }

        if (categories.Radio.Allowed is null)
        {
            mainCats.Add(RadioMainCategoryId);
        }

        return mainCats.Count == 4 ? [] : mainCats;
    }

    /// <summary>Mirrors the reference implementation's `Categories::get_cats`.</summary>
    internal static List<int> ToCategoryIds(CategoryFilter categories)
    {
        if (!HasAnyExplicitList(categories))
        {
            return [];
        }

        return Expand(categories.Audio, LegacyCategoryGroups.Audiobook)
            .Concat(Expand(categories.Ebook, LegacyCategoryGroups.Ebook))
            .Concat(Expand(categories.Musicology, LegacyCategoryGroups.Musicology))
            .Concat(Expand(categories.Radio, LegacyCategoryGroups.Radio))
            .Select(c => (int)c)
            .ToList();

        static IEnumerable<LegacyCategory> Expand(MainTypeCategoryFilter filter, IReadOnlySet<LegacyCategory> all) =>
            filter.Allowed ?? all;
    }

    private static bool HasAnyExplicitList(CategoryFilter categories) =>
        HasExplicitList(categories.Audio) || HasExplicitList(categories.Ebook)
        || HasExplicitList(categories.Musicology) || HasExplicitList(categories.Radio);

    private static bool HasExplicitList(MainTypeCategoryFilter filter) => filter.Allowed is { Count: > 0 };
}
