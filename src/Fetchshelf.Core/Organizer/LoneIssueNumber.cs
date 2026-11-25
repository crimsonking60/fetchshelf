using System.Globalization;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// The number of a single magazine issue, a torrent of its own, in a series library whose MAM series gives none: read from
/// its file name, or else the torrent's title (<see cref="MagazineIssueName"/>), so it sorts among the series' other
/// issues. It is numbered the way the series folder's numbered files mostly are (<c>#005.04</c> by volume and number,
/// <c>#1981.04</c> by year and month, <c>#038</c> by issue), when the issue's name gives that; otherwise as a magazine run
/// is: by issue number, else year and month, else volume and number. Pure.
/// </summary>
public static partial class LoneIssueNumber
{
    /// <param name="names">The issue's file name without its extension, then the torrent's title.</param>
    /// <param name="siblingFileNames">The names of the files in the series folder.</param>
    public static SeriesEntry? For(IEnumerable<string> names, IEnumerable<string> siblingFileNames)
    {
        if (names.Select(MagazineIssueName.Read).FirstOrDefault(i => i is not null) is not { } issue)
        {
            return null;
        }

        var used = siblingFileNames
            .Select(name => SeriesNumber().Match(name))
            .Where(m => m.Success)
            .Select(m => NumberingOf(m.Groups["number"].Value))
            .Where(n => n != MagazineNumbering.None)
            .GroupBy(n => n)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault(MagazineNumbering.None);
        var order = used == MagazineNumbering.None ? CollectionSplitter.Numberings : [used, .. CollectionSplitter.Numberings];
        return order.Select(n => CollectionSplitter.Entry(issue, n)).FirstOrDefault(e => e is not null);
    }

    /// <summary>Which numbering a number is in: a whole one is an issue's, a year's is from 1800, a volume's below.</summary>
    private static MagazineNumbering NumberingOf(string number)
    {
        var dot = number.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return MagazineNumbering.IssueNumber;
        }

        return int.TryParse(number[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var whole) && whole >= 1800
            ? MagazineNumbering.YearAndMonth
            : MagazineNumbering.VolumeAndNumber;
    }

    /// <summary>A series library file's number: <c>Series #005.04 - Title</c>, or a range's first, <c>#281-282</c>.</summary>
    [GeneratedRegex(@" #(?<number>\d+(?:\.\d+)?)(?:-\S+)? - ")]
    private static partial Regex SeriesNumber();

    /// <summary>
    /// Whether <paramref name="linkedName"/> is <paramref name="expectedName"/> with a number added after its series, as
    /// a lone issue is linked: <c>Series #005.04 - Title.cbr</c> for <c>Series - Title.cbr</c>.
    /// </summary>
    public static bool IsNumbered(string linkedName, string expectedName) =>
        SeriesNumber().Match(linkedName) is { Success: true } m
        && string.Equals(linkedName[..m.Index] + " - " + linkedName[(m.Index + m.Length)..], expectedName, StringComparison.Ordinal);
}
