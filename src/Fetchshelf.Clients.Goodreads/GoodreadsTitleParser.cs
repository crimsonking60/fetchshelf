using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Clients.Goodreads;

/// <summary>Extracts a trailing series suffix from a Goodreads feed item title, Fetchshelf-spec.md §7.8: "series info is extracted from titles like 'Title (Series, #3)'."</summary>
public static partial class GoodreadsTitleParser
{
    [GeneratedRegex(@"^(.*?) \(([^)]*?),? #?(\d+(?:\.\d+)?)\)$")]
    private static partial Regex SeriesSuffixPattern();

    /// <summary>
    /// Splits a raw title into its base title and an optional series name/number, if the title ends
    /// with a <c>"(Series, #N)"</c>-shaped suffix. Returns the title unchanged with a null series
    /// otherwise.
    /// </summary>
    public static (string Title, string? SeriesName, double? SeriesNumber) ExtractSeries(string rawTitle)
    {
        var match = SeriesSuffixPattern().Match(rawTitle);
        if (!match.Success)
        {
            return (rawTitle, null, null);
        }

        var title = match.Groups[1].Value;
        var seriesName = match.Groups[2].Value;
        var seriesNumber = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return (title, seriesName, seriesNumber);
    }

    /// <summary>HTML-unescapes text pulled from the feed (title, author name, series name).</summary>
    public static string Unescape(string value) => WebUtility.HtmlDecode(value);
}
