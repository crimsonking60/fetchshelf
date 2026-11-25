using System.Globalization;
using System.Text.RegularExpressions;

namespace Fetchshelf.Core.Organizer;

/// <summary>
/// What a magazine issue's file name says (plan <c>magazine-runs.md</c>): the magazine's name, when the name gives one,
/// and the issue's numbering, any of an issue number, a volume and number, and a year and month. Each may be a double
/// issue, with its last number too.
/// </summary>
/// <param name="Magazine">The magazine's name as the file spells it; null when the name gives none.</param>
/// <param name="NameInFront">Whether the name starts with the magazine's name, rather than with its date.</param>
public sealed record MagazineIssue(
    string? Magazine,
    bool NameInFront,
    int? Issue,
    int? IssueLast,
    int? Volume,
    int? Number,
    int? NumberLast,
    int? Year,
    int? Month,
    int? MonthLast)
{
    /// <summary>Whether this is the same issue as <paramref name="other"/>: every numbering both have is the same.</summary>
    public bool SameIssueAs(MagazineIssue other) =>
        (Issue is null || other.Issue is null || Issue == other.Issue)
        && (Volume is null || other.Volume is null || (Volume, Number) == (other.Volume, other.Number))
        && (Year is null || Month is null || other.Year is null || other.Month is null || (Year, Month) == (other.Year, other.Month));
}

/// <summary>
/// Reads a magazine issue's file name: <c>Lantern Issue 1</c>, <c>Lantern No 268 December 2022</c>, <c>Lantern 1967
/// Issue 1 (November 09)</c>, <c>Lantern v10n04 (Apr 1986)</c>, <c>Lantern v78n01 1990-01</c>, <c>Lantern 2013 03</c>,
/// <c>Lantern 01 Jan 1950</c>, <c>Lantern 1 Fall 1978</c>, <c>Lantern 10 1927</c>, <c>1953, 10 (October), NSM</c> and
/// <c>2019 01-02 (Lantern Book 136)</c>, with double issues such as <c>v14n11-12</c>, <c>Issue 281-282</c> and <c>2013
/// 01-02</c>. Pure.
/// </summary>
public static partial class MagazineIssueName
{
    /// <summary>
    /// The issue the name describes; null when it isn't one. The name is an issue when its text outside brackets has an
    /// issue number, a volume and number, or a year and month. Brackets may add the date or the magazine's name. A bare
    /// number counts as an issue number only when a date follows it (<c>Lantern 001 June 2010</c>), so <c>Lantern
    /// 683</c> and <c>Lantern 683 (Jan 2009)</c> are not issues.
    /// </summary>
    public static MagazineIssue? Read(string name)
    {
        var text = Spaces().Replace(name.Replace('_', ' '), " ").Trim();
        var brackets = BracketGroup().Matches(text).Select(m => m.Groups["inner"].Value.Trim()).Where(b => b.Length > 0).ToList();
        var main = Spaces().Replace(BracketGroup().Replace(text, " "), " ").Trim();

        var starts = new List<int>();
        int? volume = null, number = null, numberLast = null, issue = null, issueLast = null;
        if (VolumeNumber().Match(main) is { Success: true } vn)
        {
            (volume, number, numberLast) = (Int(vn.Groups["volume"]), Int(vn.Groups["number"]), LastAbove(vn.Groups["last"], Int(vn.Groups["number"])));
            starts.Add(vn.Index);
        }

        var marker = IssueMarker().Match(main);
        if (marker.Success)
        {
            (issue, issueLast) = (Int(marker.Groups["issue"]), LastAbove(marker.Groups["last"], Int(marker.Groups["issue"])));
            starts.Add(marker.Index);
        }

        var date = ReadDate(main, marker.Success ? marker : null);
        var dateInMain = date is not null;
        if (date is { } inMain)
        {
            starts.Add(inMain.Index);
            if (inMain.BareIssue is { } bare && issue is null)
            {
                issue = bare;
            }
        }
        else
        {
            date = brackets.Select(b => ReadDate(b, null)).FirstOrDefault(d => d is not null);
        }

        if (issue is null && volume is null && !dateInMain)
        {
            return null;
        }

        int? year = date?.Year, month = date?.Month, monthLast = date?.MonthLast;

        // A month in brackets with the year outside them: "Lantern 1967 Issue 1 (November 09)".
        if (month is null && brackets.Select(b => MonthAtStart().Match(b)).FirstOrDefault(m => m.Success) is { } bracketMonth)
        {
            month = MonthNumber(bracketMonth.Groups["month"].Value);
        }

        string magazine;
        var nameInFront = true;
        if (date is { AtStart: true } atStart && dateInMain)
        {
            magazine = main[(atStart.Index + atStart.Length)..];
            nameInFront = false;
        }
        else
        {
            magazine = main[..starts.Min()];
        }

        magazine = magazine.Trim(TrimmedFromName);
        if (TrailingYear().Match(magazine) is { Success: true } trailing)
        {
            year ??= Int(trailing.Groups["year"]);
            magazine = magazine[..trailing.Index].Trim(TrimmedFromName);
        }

        if (year is null && main.Length > 0 && LoneYear().Match(main) is { Success: true } lone)
        {
            year = Int(lone.Groups["year"]);
        }

        if (magazine.Length == 0)
        {
            nameInFront = false;
            magazine = brackets
                .Where(b => b.Any(char.IsLetter) && ReadDate(b, null) is null && !MonthAtStart().IsMatch(b))
                .Select(b => BookSuffix().Replace(b, "").Trim(TrimmedFromName))
                .FirstOrDefault(b => b.Length > 0) ?? "";
        }

        return new MagazineIssue(magazine.Length == 0 ? null : magazine, nameInFront, issue, issueLast, volume, number, numberLast, year, month, monthLast);
    }

    private static readonly char[] TrimmedFromName = [' ', '-', '_', '.', ',', '#', ':', '–'];

    private sealed record DateMatch(int Year, int Month, int? MonthLast, int Index, int Length, bool AtStart, int? BareIssue);

    /// <summary>A year and month in <paramref name="text"/>, in the first of the forms that matches.</summary>
    private static DateMatch? ReadDate(string text, Match? marker)
    {
        if (YearMonth().Match(text) is { Success: true } ym)
        {
            var month = Int(ym.Groups["month"]);
            return new DateMatch(Int(ym.Groups["year"]), month, LastAbove(ym.Groups["last"], month), ym.Index, ym.Length, ym.Index == 0, null);
        }

        if (YearCommaMonth().Match(text) is { Success: true } yc)
        {
            return new DateMatch(Int(yc.Groups["year"]), Int(yc.Groups["month"]), null, yc.Index, yc.Length, AtStart: true, null);
        }

        if (MonthNameYear().Match(text) is { Success: true } named)
        {
            var month = MonthNumber(named.Groups["month"].Value);
            var last = named.Groups["last"].Success ? MonthNumber(named.Groups["last"].Value) : (int?)null;
            int? bare = null;
            var (index, length) = (named.Groups["month"].Index, named.Index + named.Length - named.Groups["month"].Index);
            if (named.Groups["lead"].Success)
            {
                // "01 Jan 1950" is one month; "001 June 2010" is issue 1 of June 2010.
                var lead = Int(named.Groups["lead"]);
                if (lead != month)
                {
                    bare = lead;
                }

                (index, length) = (named.Index, named.Length);
            }

            return new DateMatch(Int(named.Groups["year"]), month, last > month ? last : null, index, length, index == 0, bare);
        }

        if (MonthYearAtEnd().Match(text) is { Success: true } my
            && (marker is null || my.Index >= marker.Index + marker.Length || my.Index + my.Length <= marker.Index))
        {
            return new DateMatch(Int(my.Groups["year"]), Int(my.Groups["month"]), null, my.Index, my.Length, my.Index == 0, null);
        }

        return null;
    }

    /// <summary>A month's number from its name or a season's: spring 3, summer 6, fall 9, winter 12.</summary>
    private static int MonthNumber(string name) => name.ToLowerInvariant() switch
    {
        var n when n.StartsWith("spring", StringComparison.Ordinal) => 3,
        var n when n.StartsWith("summer", StringComparison.Ordinal) => 6,
        var n when n.StartsWith("fall", StringComparison.Ordinal) || n.StartsWith("autumn", StringComparison.Ordinal) => 9,
        var n when n.StartsWith("winter", StringComparison.Ordinal) => 12,
        var n => Array.IndexOf(MonthPrefixes, n[..3]) + 1,
    };

    private static readonly string[] MonthPrefixes = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static int Int(Group group) => int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static int? LastAbove(Group group, int first) => group.Success && Int(group) > first ? Int(group) : null;

    private const string Months =
        @"(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?|spring|summer|fall|autumn|winter)";

    private const string AYear = @"(?:18|19|20)\d\d";

    [GeneratedRegex(@"[\(\[](?<inner>[^\(\)\[\]]*)[\)\]]")]
    private static partial Regex BracketGroup();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    // v10n04, v034n01, v035 n01, v14n11-12, v87n04&05.
    [GeneratedRegex(@"\bv(?<volume>\d{1,3})\s?n(?<number>\d{1,3})(?:\s?[-&]\s?(?<last>\d{1,3}))?(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeNumber();

    // Issue 1, Issue1, No 268, No. 268, Issue 281-282.
    [GeneratedRegex(@"\b(?:issue\s?|no\.?\s?)(?<issue>\d{1,5})(?:\s?[-&]\s?(?<last>\d{1,5}))?(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex IssueMarker();

    // 1990-01, 1990 01, 1990.01, 2013 01-02, 1994-10&11.
    [GeneratedRegex(@"(?<!\d)(?<year>" + AYear + @")[-. ](?<month>0[1-9]|1[0-2])(?:[-&](?<last>0[1-9]|1[0-2]))?(?!\d)")]
    private static partial Regex YearMonth();

    // 1953, 10 at the start of the name.
    [GeneratedRegex(@"^(?<year>" + AYear + @"),\s*(?<month>1[0-2]|0?[1-9])(?!\d)")]
    private static partial Regex YearCommaMonth();

    // December 2022, Apr-May 2010, Fall 2000, 01 Jan 1950, 001 June 2010.
    [GeneratedRegex(@"(?:(?<!\d)(?<lead>\d{1,4})\s+)?\b(?<month>" + Months + @")\.?(?:\s?[-/&]\s?(?<last>" + Months + @")\.?)?(?:\s+\d{1,2})?,?\s+(?<year>" + AYear + @")(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex MonthNameYear();

    // 10 1927, -07-1961 at the end of the name; not a volume's number (Vol.1 1930).
    [GeneratedRegex(@"(?<!\d)(?<!\bvol\.?\s?)(?<!\bv)(?<month>1[0-2]|0?[1-9])[\s.-](?<year>" + AYear + @")$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthYearAtEnd();

    [GeneratedRegex(@"^(?<month>" + Months + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthAtStart();

    [GeneratedRegex(@"\s(?<year>" + AYear + @")$")]
    private static partial Regex TrailingYear();

    // A year on its own, not one of a range such as 1981-2025.
    [GeneratedRegex(@"(?<![\d-])(?<year>" + AYear + @")(?![\d-])")]
    private static partial Regex LoneYear();

    [GeneratedRegex(@"\s+book\s+\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex BookSuffix();
}
