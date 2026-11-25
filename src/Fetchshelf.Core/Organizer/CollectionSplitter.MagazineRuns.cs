using System.Globalization;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Magazine runs (plan <c>magazine-runs.md</c>): one torrent of many issues of one magazine.</summary>
public static partial class CollectionSplitter
{
    /// <summary>The numberings tried for a run, in order: the first that numbers every issue on its own is used.</summary>
    internal static readonly MagazineNumbering[] Numberings =
        [MagazineNumbering.IssueNumber, MagazineNumbering.YearAndMonth, MagazineNumbering.VolumeAndNumber];

    /// <summary>
    /// The books of a magazine run, with the magazine as their series; null when the torrent isn't one. It is one when
    /// at least three quarters of its books, and at least <see cref="MinimumNumberedBooks"/>, are magazine issues
    /// (<see cref="MagazineIssueName"/>), and at least that many different issues. Every issue is numbered the same
    /// way: by issue number, else by year and month, else by volume and number, the first that gives every issue a
    /// number no other issue has.
    /// </summary>
    private static CollectionSplit? MagazineRun(
        TorrentMetadata collection, List<FileGroup> groups, IReadOnlyList<TorrentFileEntry> files, IReadOnlyList<string> ebookTypes,
        Func<string, EpubInfo?>? readEpub)
    {
        var issues = groups.ToDictionary(g => g, g => MagazineIssueName.Read(g.Name));
        var read = issues.Where(i => i.Value is not null).ToDictionary(i => i.Key, i => i.Value!);
        if (read.Count < MinimumNumberedBooks || read.Count * 4 < groups.Count * 3)
        {
            return null;
        }

        var numbering = Numberings.FirstOrDefault(n => NumberingProblem(read, n) is null, MagazineNumbering.None);
        var different = numbering == MagazineNumbering.None
            ? read.Values.Select(i => (i.Issue, i.Volume, i.Number, i.Year, i.Month)).Distinct().Count()
            : read.Values.Select(i => Key(i, numbering)).Distinct().Count();
        if (different < MinimumNumberedBooks)
        {
            return null; // one issue in several files, such as its cover and its pages
        }

        var warnings = new List<SplitWarning>();
        var series = RunSeriesName(collection, read.Values, warnings);

        // The same issue twice, under two names: one title, so that only its best copy is kept.
        var titles = groups.ToDictionary(g => g, g => RunTitle(g, issues[g], series, collection.Authors));
        if (numbering != MagazineNumbering.None)
        {
            foreach (var same in read.GroupBy(r => Key(r.Value, numbering)).Where(g => g.Count() > 1))
            {
                var title = same.Select(s => titles[s.Key]).Order(StringComparer.Ordinal).First();
                foreach (var (group, _) in same)
                {
                    titles[group] = title;
                }
            }
        }

        AddNumberingWarnings(groups, read, numbering, warnings);

        var sizes = files.ToDictionary(f => f.Path, f => f.SizeBytes, StringComparer.Ordinal);
        var result = new List<(CollectionBook Book, decimal? Position)>();
        foreach (var (group, index) in groups.OrderBy(g => g.Name, StringComparer.Ordinal).Select((g, i) => (g, i)))
        {
            var (format, chosen) = BestFormat(group, ebookTypes);
            // An issue's epub often carries a story's title or the year's, so only its ISBN and description are used.
            var epub = string.Equals(format, "epub", StringComparison.OrdinalIgnoreCase) ? readEpub?.Invoke(chosen[0]) : null;
            var entry = issues[group] is { } issue ? Entry(issue, numbering) : null;
            var metadata = BookMetadata(collection, titles[group], collection.Authors, series, entry, epub, format, chosen, sizes);
            result.Add((new CollectionBook(index + 1, metadata, chosen, format.ToLowerInvariant(), null), null));
        }

        return new CollectionSplit(WithCovers(WithoutRepeats(result, ebookTypes), files), SplitStrategy.MagazineRun, numbering, null, warnings);
    }

    /// <summary>
    /// Why <paramref name="numbering"/> can't number the run: an issue without it, or two different issues with the
    /// same number; null when it can.
    /// </summary>
    private static string? NumberingProblem(Dictionary<FileGroup, MagazineIssue> read, MagazineNumbering numbering)
    {
        var missing = read.Count(r => Key(r.Value, numbering) is null);
        if (missing > 0)
        {
            return $"{missing} issues have no {NumberingName(numbering)}";
        }

        var clash = read.GroupBy(r => Key(r.Value, numbering)).FirstOrDefault(g => g.Any(r => !r.Value.SameIssueAs(g.First().Value)));
        return clash is null ? null : $"two issues share the {NumberingName(numbering)} {FormatKey(clash.Key!.Value)}";
    }

    /// <summary>The warnings about a run's numbering: none found, numberings that only some issues have, books that aren't issues.</summary>
    private static void AddNumberingWarnings(
        List<FileGroup> groups, Dictionary<FileGroup, MagazineIssue> read, MagazineNumbering numbering, List<SplitWarning> warnings)
    {
        if (numbering == MagazineNumbering.None)
        {
            var problems = Numberings.Select(n => NumberingProblem(read, n)).OfType<string>();
            warnings.Add(new SplitWarning(
                SplitWarningKind.NumberingNotRecognized,
                $"the issues have no numbering that gives each one a number of its own ({string.Join("; ", problems)}), so they are not numbered",
                Examples(read.Keys.Select(g => g.Name))));
        }

        // A numbering tried before the one used, that some issues have and others don't: the names mix numberings.
        foreach (var partial in Numberings.TakeWhile(n => n != numbering))
        {
            var without = read.Where(r => Key(r.Value, partial) is null).Select(r => r.Key.Name).ToList();
            if (without.Count > 0 && without.Count < read.Count)
            {
                warnings.Add(new SplitWarning(
                    SplitWarningKind.InconsistentNumbering,
                    $"{read.Count - without.Count} of {read.Count} issues have {(partial == MagazineNumbering.IssueNumber ? "an" : "a")} {NumberingName(partial)}, the others don't",
                    Examples(without)));
            }
        }

        var unread = groups.Where(g => !read.ContainsKey(g)).Select(g => g.Name).ToList();
        if (unread.Count > 0)
        {
            warnings.Add(new SplitWarning(
                SplitWarningKind.InconsistentNumbering,
                $"{unread.Count} of {groups.Count} books don't read as issues and have no number",
                Examples(unread)));
        }
    }

    /// <summary>
    /// The run's series: MAM's series when MAM lists exactly one, as it names a magazine's torrents alike; else the
    /// magazine name most issues use, spelled as most spell it; else the torrent's title without its years and words such
    /// as <i>Issues</i>.
    /// </summary>
    private static string RunSeriesName(TorrentMetadata collection, IEnumerable<MagazineIssue> issues, List<SplitWarning> warnings)
    {
        var names = issues.Select(i => i.Magazine).OfType<string>()
            .GroupBy(TitleNormalizer.Normalize)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.GroupBy(n => n, StringComparer.Ordinal).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key)
            .ToList();
        if (collection.Series.Count == 1)
        {
            return collection.Series[0].Name;
        }

        if (names.Count > 1)
        {
            warnings.Add(new SplitWarning(
                SplitWarningKind.SeveralMagazineNames,
                $"the issues use {names.Count} magazine names and MAM lists {(collection.Series.Count == 0 ? "no series" : "several series")}, so the series is the most common name, {names[0]}",
                names.Take(3).ToList()));
        }

        if (names.Count > 0)
        {
            return names[0];
        }

        var title = Clean(collection.Title, collection.Authors);
        var bare = Spaces().Replace(RunTitleWords().Replace(title, " "), " ").Trim(' ', '-', '–', ',', ':', '&');
        return bare.Length > 0 ? bare : title;
    }

    /// <summary>
    /// An issue's title: its cleaned name, with the series in front when the name doesn't start with the magazine's
    /// name (<c>2019 01-02</c> is <c>Nightfall Stories 2019 01-02</c>). A book that isn't an issue keeps its name.
    /// </summary>
    private static string RunTitle(FileGroup group, MagazineIssue? issue, string series, IReadOnlyList<string> authors)
    {
        var cleaned = Spaces().Replace(Clean(group.Name, authors).Replace('_', ' '), " ").Trim();
        if (issue is null)
        {
            return cleaned;
        }

        return issue is { Magazine: { } magazine, NameInFront: true }
            && TitleNormalizer.Normalize(cleaned).StartsWith(TitleNormalizer.Normalize(magazine), StringComparison.Ordinal)
            ? cleaned
            : $"{series} {cleaned}";
    }

    /// <summary>An issue's number by <paramref name="numbering"/>; null when its name doesn't give it.</summary>
    private static decimal? Key(MagazineIssue issue, MagazineNumbering numbering) => numbering switch
    {
        MagazineNumbering.IssueNumber => issue.Issue,
        MagazineNumbering.YearAndMonth => issue is { Year: { } year, Month: { } month } ? Decimal(year, month) : null,
        MagazineNumbering.VolumeAndNumber => issue is { Volume: { } volume, Number: { } number and <= 99 } ? Decimal(volume, number) : null,
        _ => null,
    };

    /// <summary>An issue's series entry: its number, or for a double issue the range of both.</summary>
    internal static SeriesEntry? Entry(MagazineIssue issue, MagazineNumbering numbering)
    {
        if (Key(issue, numbering) is not { } first)
        {
            return null;
        }

        decimal? last = numbering switch
        {
            MagazineNumbering.IssueNumber => issue.IssueLast,
            MagazineNumbering.YearAndMonth => issue.MonthLast is { } month ? Decimal(issue.Year!.Value, month) : null,
            MagazineNumbering.VolumeAndNumber => issue.NumberLast is { } number and <= 99 ? Decimal(issue.Volume!.Value, number) : null,
            _ => null,
        };
        return last is { } l ? SeriesEntry.ForRange(first, l) : SeriesEntry.ForNumber(first, FormatKey(first));
    }

    /// <summary>A whole and a two-digit part, as 1990.01 or 10.04, kept with both digits.</summary>
    private static decimal Decimal(int whole, int part) =>
        decimal.Parse(string.Create(CultureInfo.InvariantCulture, $"{whole}.{part:D2}"), CultureInfo.InvariantCulture);

    private static string FormatKey(decimal key) => key.ToString(CultureInfo.InvariantCulture);

    private static string NumberingName(MagazineNumbering numbering) => numbering switch
    {
        MagazineNumbering.IssueNumber => "issue number",
        MagazineNumbering.YearAndMonth => "year and month",
        MagazineNumbering.VolumeAndNumber => "volume and number",
        _ => "numbering",
    };

    // What a run's title holds besides the magazine's name: years and ranges of them, and words for a run.
    [GeneratedRegex(@"\b(?:(?:18|19|20)\d\d(?:\s*-\s*(?:18|19|20)\d\d)?|all\s+issues|issues|complete|full\s+year|collection)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RunTitleWords();
}
