using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Metadata;

/// <summary>A grade for a torrent's formats, and what is wrong with them in plain words; null when nothing is.</summary>
public sealed record FormatQualityRating(MetadataQualityGrade Grade, string? Issue);

/// <summary>
/// Rates a torrent's file types against the format list for its media type, such as `ebook_types`. A torrent with the
/// preferred format is good. One with only other formats in the list is lacking, and one with none of them is poor:
/// Fetchshelf won't download it. A torrent whose file types MAM doesn't list is left to <see cref="MetadataQuality"/>,
/// which rates it poor for that already.
/// </summary>
/// <remarks>
/// The preferred format is the first in the list, except that ebooks, comics and manga share `ebook_types`: a comic or
/// manga prefers the first comic format in it, such as `cbz`, and any other ebook the first format that isn't a comic
/// format, such as `epub`. A list without such a format falls back to its first. Every format in the list still counts
/// as accepted, as it does for selection, so a comic in `pdf` is lacking, not poor.
/// </remarks>
public static class FormatQuality
{
    /// <summary>Comic book archive formats, which only comics and manga prefer.</summary>
    public static readonly IReadOnlySet<string> ComicFormats = new HashSet<string>(["cbz", "cbr", "cb7", "cbt"], StringComparer.OrdinalIgnoreCase);

    /// <param name="preferenceList">The format list for the torrent's media type, best first.</param>
    /// <param name="fileTypes">The torrent's file types, as MAM lists them.</param>
    /// <param name="mediaType">The torrent's media type, which decides the preferred format of the shared ebook list.</param>
    /// <param name="settingName">The setting that holds the list, such as "ebook_types", for the issue's wording.</param>
    public static FormatQualityRating Rate(
        IReadOnlyList<string> preferenceList, IReadOnlyList<string> fileTypes, MediaType mediaType, string settingName)
    {
        if (fileTypes.Count == 0 || preferenceList.Count == 0)
        {
            return new FormatQualityRating(MetadataQualityGrade.Good, null);
        }

        var has = string.Join(", ", fileTypes);
        if (FormatRanker.Rank(preferenceList, fileTypes) == FormatRanker.NotPresent)
        {
            return new FormatQualityRating(
                MetadataQualityGrade.Poor,
                $"Format: none of its file types ({has}) is in your {settingName} setting ({string.Join(", ", preferenceList)}).");
        }

        var preferred = Preferred(preferenceList, mediaType);
        return FormatRanker.Rank([preferred], fileTypes) == 0
            ? new FormatQualityRating(MetadataQualityGrade.Good, null)
            : new FormatQualityRating(MetadataQualityGrade.Lacking, $"Format: no {preferred} (preferred), it has {has}.");
    }

    /// <summary>The format the torrent should have: see the remarks on <see cref="FormatQuality"/>.</summary>
    public static string Preferred(IReadOnlyList<string> preferenceList, MediaType mediaType)
    {
        Func<string, bool>? fits = mediaType switch
        {
            MediaType.ComicBook or MediaType.Manga => ComicFormats.Contains,
            MediaType.Ebook or MediaType.PeriodicalEbook => format => !ComicFormats.Contains(format),
            _ => null,
        };
        return (fits is null ? null : preferenceList.FirstOrDefault(fits)) ?? preferenceList[0];
    }

    /// <summary>The worse of two grades, so a torrent is only as good as its weakest side.</summary>
    public static MetadataQualityGrade Worse(MetadataQualityGrade first, MetadataQualityGrade second) =>
        first > second ? first : second;
}
