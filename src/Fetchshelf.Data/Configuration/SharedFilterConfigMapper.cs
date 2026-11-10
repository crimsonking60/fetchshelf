using System.Globalization;
using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Data.Configuration;

/// <summary>Maps a <see cref="SharedFilterConfig"/> onto the core <see cref="TorrentFilter"/>, resolving category/language/flag names and parsing size/date strings.</summary>
public static class SharedFilterConfigMapper
{
    public static TorrentFilter ToTorrentFilter(SharedFilterConfig config)
    {
        return new TorrentFilter
        {
            MediaTypes = config.MediaType.Count == 0 ? null : config.MediaType.Select(ParseMediaType).ToList(),
            Categories = ToCategoryFilter(config.Categories),
            Languages = config.Languages is not { Count: > 0 } ? null : config.Languages.Select(ParseLanguage).ToList(),
            Flags = config.Flags.Count == 0 ? null : ToFlagsDictionary(config.Flags),
            MinSizeBytes = config.MinSize is null ? null : ParseSize(config.MinSize),
            MaxSizeBytes = config.MaxSize is null ? null : ParseSize(config.MaxSize),
            ExcludeUploader = config.ExcludeUploader.Count == 0 ? null : config.ExcludeUploader,
            UploadedAfter = config.UploadedAfter is null ? null : ParseDate(config.UploadedAfter),
            UploadedBefore = config.UploadedBefore is null ? null : ParseDate(config.UploadedBefore),
            MinSeeders = config.MinSeeders,
            MaxSeeders = config.MaxSeeders,
            MinLeechers = config.MinLeechers,
            MaxLeechers = config.MaxLeechers,
            MinSnatched = config.MinSnatched,
            MaxSnatched = config.MaxSnatched,
            MaxAuthors = config.MaxAuthors,
        };
    }

    private static CategoryFilter ToCategoryFilter(CategoriesConfig config) => new()
    {
        Audio = ToMainTypeCategoryFilter(FilterMainType.Audio, config.Audio),
        Ebook = ToMainTypeCategoryFilter(FilterMainType.Ebook, config.Ebook),
        Musicology = ToMainTypeCategoryFilter(FilterMainType.Musicology, config.Musicology),
        Radio = ToMainTypeCategoryFilter(FilterMainType.Radio, config.Radio),
    };

    private static MainTypeCategoryFilter ToMainTypeCategoryFilter(FilterMainType mainType, CategoryListOrBool value)
    {
        if (value.Names is null)
        {
            return MainTypeCategoryFilter.All;
        }

        if (value.Names.Count == 0)
        {
            return MainTypeCategoryFilter.None;
        }

        var categories = new HashSet<LegacyCategory>();
        foreach (var name in value.Names)
        {
            if (!CategoryNameResolver.TryResolve(mainType, name, out var category))
            {
                throw new ConfigurationException($"Invalid configuration: unrecognized category name '{name}'.");
            }

            categories.Add(category);
        }

        return new MainTypeCategoryFilter { Allowed = categories };
    }

    private static MediaType ParseMediaType(string name) => Enum.TryParse<MediaType>(name, ignoreCase: true, out var mediaType)
        ? mediaType
        : throw new ConfigurationException($"Invalid configuration: unrecognized media type '{name}'.");

    private static Language ParseLanguage(string name) => LanguageNameResolver.TryResolve(name, out var language)
        ? language
        : throw new ConfigurationException($"Invalid configuration: unrecognized language '{name}'.");

    private static Dictionary<BookFlags, bool> ToFlagsDictionary(Dictionary<string, bool> flags)
    {
        var result = new Dictionary<BookFlags, bool>();
        foreach (var (name, required) in flags)
        {
            if (!FlagNameResolver.TryResolve(name, out var flag))
            {
                throw new ConfigurationException($"Invalid configuration: unrecognized flag name '{name}'.");
            }

            result[flag] = required;
        }

        return result;
    }

    private static long ParseSize(string value) => ConfigValueValidator.TryParseSize(value)
        ?? throw new ConfigurationException($"Invalid configuration: unrecognized size '{value}', {ConfigValueValidator.SizeExample}.");

    private static DateOnly ParseDate(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date
        : throw new ConfigurationException($"Invalid configuration: unrecognized date '{value}', expected yyyy-MM-dd.");
}
