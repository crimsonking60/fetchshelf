using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.AutoDownloader;

/// <summary>
/// One main type's category constraint within a shared filter's `categories` field, Fetchshelf-spec.md
/// §4.3: <c>null</c> means "all categories of this type" (`true`), an empty set means "none of this
/// type" (`false`), and a non-empty set means "only these categories".
/// </summary>
public sealed record MainTypeCategoryFilter
{
    public static readonly MainTypeCategoryFilter All = new() { Allowed = null };

    public static readonly MainTypeCategoryFilter None = new() { Allowed = new HashSet<LegacyCategory>() };

    public IReadOnlySet<LegacyCategory>? Allowed { get; init; }
}

/// <summary>
/// The `categories` field's per-main-type structure, Fetchshelf-spec.md §4.3. Defaults match the spec's own
/// documented default: audio and ebook all, musicology and radio none.
/// </summary>
public sealed record CategoryFilter
{
    public MainTypeCategoryFilter Audio { get; init; } = MainTypeCategoryFilter.All;

    public MainTypeCategoryFilter Ebook { get; init; } = MainTypeCategoryFilter.All;

    public MainTypeCategoryFilter Musicology { get; init; } = MainTypeCategoryFilter.None;

    public MainTypeCategoryFilter Radio { get; init; } = MainTypeCategoryFilter.None;
}

/// <summary>
/// The shared torrent filter fields, Fetchshelf-spec.md §4.3. A torrent matches only if every field that is
/// set is satisfied; unset fields impose no constraint, except <see cref="Categories"/> which always
/// applies its own (possibly default) per-main-type rule. Field shapes are config-source-agnostic;
/// translation from YAML text happens in `Fetchshelf.Data.Configuration`.
/// </summary>
public sealed record TorrentFilter
{
    public IReadOnlyList<MediaType>? MediaTypes { get; init; }

    public CategoryFilter Categories { get; init; } = new();

    public IReadOnlyList<Language>? Languages { get; init; }

    /// <summary>Per flag: true = required, false = forbidden, absent from the dictionary = ignored.</summary>
    public IReadOnlyDictionary<BookFlags, bool>? Flags { get; init; }

    public long? MinSizeBytes { get; init; }

    public long? MaxSizeBytes { get; init; }

    public IReadOnlyList<string>? ExcludeUploader { get; init; }

    public DateOnly? UploadedAfter { get; init; }

    public DateOnly? UploadedBefore { get; init; }

    public long? MinSeeders { get; init; }

    public long? MaxSeeders { get; init; }

    public long? MinLeechers { get; init; }

    public long? MaxLeechers { get; init; }

    public long? MinSnatched { get; init; }

    public long? MaxSnatched { get; init; }

    /// <summary>Inclusive cap on the number of listed authors; rejects anthologies. Not part of the original filter field set.</summary>
    public int? MaxAuthors { get; init; }
}

/// <summary>Maps a media type onto the shared filter's four main-type groups, Fetchshelf-spec.md §4.3.</summary>
public static class FilterMainTypeMapper
{
    public static FilterMainType FromMediaType(MediaType mediaType) => mediaType switch
    {
        MediaType.Audiobook or MediaType.PeriodicalAudiobook => FilterMainType.Audio,
        MediaType.Ebook or MediaType.ComicBook or MediaType.Manga or MediaType.PeriodicalEbook => FilterMainType.Ebook,
        MediaType.Musicology => FilterMainType.Musicology,
        MediaType.Radio => FilterMainType.Radio,
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
    };
}
