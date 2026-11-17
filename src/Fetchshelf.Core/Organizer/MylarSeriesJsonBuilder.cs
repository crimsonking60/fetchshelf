using System.Text.Json;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// Mylar's <c>series.json</c>, which Komga reads for a series folder's title. Fetchshelf knows only the series' name; Komga's
/// reader needs every other field present too, so they are written empty or as zero. <c>volume</c> is 1, for which Komga
/// adds no year to the title, and <c>status</c> is <c>Continuing</c>, as whether a series has ended isn't known.
/// </summary>
public static class MylarSeriesJsonBuilder
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <param name="seriesName">The series' name as the book's details have it, not cleaned for a folder name.</param>
    public static string Build(string seriesName) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["version"] = "1.0.2",
            ["metadata"] = new Dictionary<string, object?>
            {
                ["type"] = "comicSeries",
                ["publisher"] = string.Empty,
                ["imprint"] = null,
                ["name"] = seriesName,
                ["comicid"] = string.Empty,
                ["year"] = 0,
                ["description_text"] = null,
                ["description_formatted"] = null,
                ["volume"] = 1,
                // Required by Komga's reader, but not used by it.
                ["booktype"] = "Print",
                ["age_rating"] = null,
                ["comic_image"] = string.Empty,
                ["total_issues"] = 0,
                ["publication_run"] = string.Empty,
                ["status"] = "Continuing",
            },
        }, Options);
}
