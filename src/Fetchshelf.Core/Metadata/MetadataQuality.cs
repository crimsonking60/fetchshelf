using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Metadata;

/// <summary>How complete a torrent's metadata on MAM is.</summary>
public enum MetadataQualityGrade
{
    Good,
    Lacking,
    Poor,
}

/// <summary>What a torrent's metadata has, as far as rating it goes.</summary>
/// <param name="HasAuthor">At least one author.</param>
/// <param name="Description">The description as MAM gives it, with its markup.</param>
/// <param name="HasCover">MAM has a cover image for it.</param>
/// <param name="HasGenres">At least one of MAM's genre categories.</param>
/// <param name="IsAudio">An audiobook or periodical audiobook, which should name its narrators.</param>
/// <param name="HasNarrator">At least one narrator.</param>
/// <param name="HasIdentifier">An ISBN or ASIN.</param>
/// <param name="HasFileTypes">MAM lists the file types the torrent holds.</param>
public sealed record MetadataQualityFacts(
    bool HasAuthor, string? Description, bool HasCover, bool HasGenres, bool IsAudio, bool HasNarrator, bool HasIdentifier,
    bool HasFileTypes);

/// <summary>A grade, and what is missing in plain words, most important first.</summary>
public sealed record MetadataQualityRating(MetadataQualityGrade Grade, IReadOnlyList<string> Missing);

/// <summary>
/// Rates how complete a torrent's metadata is, which says how well Fetchshelf can name, file and describe the book.
/// A torrent without an author or without file types is poor whatever else it has: without an author it can't be
/// linked, and without file types Fetchshelf can't tell its format, so it never selects it. Otherwise every gap costs
/// points, a missing or very short description two and anything else one: up to 1 point is good, 2 lacking, and 3
/// or more poor. One small gap, such as a missing ISBN, still leaves a torrent good.
/// </summary>
public static partial class MetadataQuality
{
    /// <summary>A description with fewer characters of text than this, markup left out, counts as missing.</summary>
    public const int MinDescriptionLength = 80;

    public static MetadataQualityRating Rate(MetadataQualityFacts facts)
    {
        var missing = new List<string>();
        var points = 0;
        void Gap(bool present, string what, int cost)
        {
            if (!present)
            {
                missing.Add(what);
                points += cost;
            }
        }

        if (!facts.HasAuthor)
        {
            missing.Add("author");
        }

        if (!facts.HasFileTypes)
        {
            missing.Add("file types (Fetchshelf can't download it without them)");
        }

        var descriptionLength = TextLength(facts.Description);
        Gap(descriptionLength >= MinDescriptionLength, descriptionLength == 0 ? "description" : "a real description (it is very short)", 2);
        Gap(facts.HasCover, "cover", 1);
        Gap(facts.HasGenres, "genres", 1);
        if (facts.IsAudio)
        {
            Gap(facts.HasNarrator, "narrator", 1);
        }

        Gap(facts.HasIdentifier, "ISBN or ASIN", 1);

        var grade = !facts.HasAuthor || !facts.HasFileTypes || points >= 3 ? MetadataQualityGrade.Poor
            : points == 2 ? MetadataQualityGrade.Lacking
            : MetadataQualityGrade.Good;
        return new MetadataQualityRating(grade, missing);
    }

    /// <summary>How many characters of text <paramref name="description"/> has, leaving out HTML, BBCode and extra whitespace.</summary>
    public static int TextLength(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return 0;
        }

        var text = BbCode().Replace(Html().Replace(description, " "), " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim().Length;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Html();

    [GeneratedRegex(@"\[/?[a-zA-Z*][^\]]*\]")]
    private static partial Regex BbCode();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
