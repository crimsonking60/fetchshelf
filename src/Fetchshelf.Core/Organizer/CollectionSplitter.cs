using System.Globalization;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>A file of a torrent: its path inside the torrent, and its size.</summary>
public sealed record TorrentFileEntry(string Path, long SizeBytes);

/// <summary>One book of a split collection.</summary>
/// <param name="Position">Its place in the collection, from 1, by its folder or file name: the end of its record's id.</param>
/// <param name="Metadata">The collection's details with the book's own title, series number, ISBN, description and format.</param>
/// <param name="Files">The book's files in its chosen format, as paths inside the torrent.</param>
/// <param name="Format">Its chosen format, such as <c>epub</c>.</param>
/// <param name="CoverFile">An image of the book's own folder, its cover, as a path inside the torrent; null when there is none.</param>
public sealed record CollectionBook(int Position, TorrentMetadata Metadata, IReadOnlyList<string> Files, string Format, string? CoverFile);

/// <summary>
/// Tells whether a torrent is a collection of books, and splits it into them (plan <c>collection-torrents.md</c>). Only
/// the ebook side is split: ebooks, comics, manga and periodicals. Pure: reading an epub is passed in.
/// </summary>
public static partial class CollectionSplitter
{
    /// <summary>The fewest books with one series name in their file names that make a torrent without MAM's range a collection.</summary>
    public const int MinimumNumberedBooks = 3;

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// The books of a collection; null when the torrent isn't one. It is one when its files of a listed ebook format fall
    /// into at least two books, and MAM's series has a range such as <c>1-40</c>, or at least
    /// <see cref="MinimumNumberedBooks"/> of the books are named <c>&lt;series&gt; &lt;number&gt;</c> with one series name.
    /// A book is a sub-folder below the torrent's own folder, when every such file sits in one; else the files sharing
    /// a name, whatever their format. Each book takes its own best format by <paramref name="ebookTypes"/>.
    /// </summary>
    /// <param name="readEpub">Reads an epub inside the torrent, by its path there; null reads none.</param>
    public static IReadOnlyList<CollectionBook>? Split(
        TorrentMetadata collection, IReadOnlyList<TorrentFileEntry> files, IReadOnlyList<string> ebookTypes,
        Func<string, EpubInfo?>? readEpub = null)
    {
        if (!IsEbookSide(collection.MediaType))
        {
            return null;
        }

        var books = files.Where(f => ebookTypes.Contains(Extension(f.Path), StringComparer.OrdinalIgnoreCase)).ToList();
        var groups = Group(books.Select(f => f.Path).ToList(), collection.Authors);
        if (groups is null || groups.Count < 2)
        {
            return null;
        }

        var range = collection.Series.FirstOrDefault(s => s.Entries.Any(e => e.Kind == SeriesEntryKind.Range && e.LastNumber > e.FirstNumber));
        var numbered = groups.ToDictionary(g => g, g => NumberedName(Clean(g.Name, collection.Authors)));
        var largestNamedSeries = numbered.Values.OfType<(string Series, decimal Number)>()
            .GroupBy(n => TitleNormalizer.Normalize(n.Series))
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
        if (range is null && largestNamedSeries < MinimumNumberedBooks)
        {
            return null;
        }

        // One series spelled several ways in its file names, such as "Spider-man" and "Spider-Man", is named as most are.
        var spellings = numbered.Values.OfType<(string Series, decimal Number)>()
            .GroupBy(n => TitleNormalizer.Normalize(n.Series))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(n => n.Series, StringComparer.Ordinal).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key);
        var listed = range is null ? [] : NumberedList(collection.Description);
        var sizes = files.ToDictionary(f => f.Path, f => f.SizeBytes, StringComparer.Ordinal);
        var result = new List<CollectionBook>();
        foreach (var (group, index) in groups.OrderBy(g => g.Name, StringComparer.Ordinal).Select((g, i) => (g, i)))
        {
            var format = FormatRanker.SelectBestExtension(ebookTypes, group.Files.Select(System.IO.Path.GetExtension).OfType<string>().ToList())!;
            var chosen = group.Files.Where(f => string.Equals(Extension(f), format, StringComparison.OrdinalIgnoreCase)).ToList();
            var epub = string.Equals(format, "epub", StringComparison.OrdinalIgnoreCase) ? readEpub?.Invoke(chosen[0]) : null;
            var cleaned = Clean(group.Name, collection.Authors);

            string title;
            string? seriesName;
            decimal? number;
            if (range is not null)
            {
                seriesName = range.Name;
                var leading = LeadingNumber(cleaned);
                title = epub?.Title ?? leading?.Title ?? cleaned;
                number = epub is { SeriesName: { } epubSeries, SeriesNumber: { } epubNumber } && SameName(epubSeries, range.Name) ? epubNumber
                    : leading?.Number ?? numbered[group]?.Number ?? FromList(listed, title);
            }
            else if (numbered[group] is var (series, issue))
            {
                // The folder may say which volume of the series its issues are; the torrent's title may name the series
                // better than its file names do.
                var better = BetterSeriesName(WithFolderVolume(spellings[TitleNormalizer.Normalize(series)], group.Files[0]), collection);
                (title, seriesName, number) = (
                    better != series && cleaned.StartsWith(series, StringComparison.Ordinal) ? better + cleaned[series.Length..] : cleaned,
                    better,
                    issue);
            }
            else
            {
                (title, seriesName, number) = (epub?.Title ?? cleaned, null, null);
            }

            var metadata = collection with
            {
                Title = title,
                Series = seriesName is null ? [] : [new Series { Name = seriesName, Entries = number is { } n ? [SeriesEntry.ForNumber(n, n.ToString(CultureInfo.InvariantCulture))] : [] }],
                Isbn = epub?.Isbn,
                Asin = null,
                Description = epub?.Description,
                FileTypes = [format.ToLowerInvariant()],
                FileCount = chosen.Count,
                TotalSizeBytes = chosen.Sum(f => sizes.GetValueOrDefault(f)),
            };
            result.Add(new CollectionBook(index + 1, metadata, chosen, format.ToLowerInvariant(), null));
        }

        return WithCovers(WithoutRepeats(result, ebookTypes), files);
    }

    /// <summary>
    /// The books with one of each kept when the torrent holds a book twice, such as a series in two folders spelled
    /// differently: the same title, series and number. The one kept has the best format, then the most bytes; the rest
    /// are numbered again in order.
    /// </summary>
    private static List<CollectionBook> WithoutRepeats(List<CollectionBook> books, IReadOnlyList<string> ebookTypes) =>
        books
            .GroupBy(b => (
                Title: b.Metadata.Title.ToUpperInvariant(),
                Series: b.Metadata.Series.FirstOrDefault()?.Name.ToUpperInvariant(),
                Number: b.Metadata.Series.FirstOrDefault()?.Entries.FirstOrDefault()?.FirstNumber))
            .Select(g => g
                .OrderBy(b => FormatRank(ebookTypes, b.Format))
                .ThenByDescending(b => b.Metadata.TotalSizeBytes)
                .ThenBy(b => b.Position)
                .First())
            .OrderBy(b => b.Position)
            .Select((b, i) => b with { Position = i + 1 })
            .ToList();

    private static int FormatRank(IReadOnlyList<string> ebookTypes, string format)
    {
        var rank = ebookTypes.ToList().FindIndex(t => string.Equals(t, format, StringComparison.OrdinalIgnoreCase));
        return rank < 0 ? int.MaxValue : rank;
    }

    /// <summary>Whether a media type is on the ebook side, the one split.</summary>
    public static bool IsEbookSide(MediaType mediaType) =>
        mediaType is MediaType.Ebook or MediaType.ComicBook or MediaType.Manga or MediaType.PeriodicalEbook;

    private sealed record FileGroup(string Name, List<string> Files);

    /// <summary>
    /// The books: one per sub-folder below the torrent's own folder when every file sits in one and each folder's files
    /// share one name (one book in its formats, as a Calibre export has it); else one per file name in a folder, whatever
    /// its format, and with release tags in brackets left out, so <c>Saga 001 (Digital).cbz</c> and <c>Saga 001.cbr</c> are one
    /// book. Null when nothing can be told apart, or a disc folder says it is one book across discs.
    /// </summary>
    private static List<FileGroup>? Group(List<string> paths, IReadOnlyList<string> authors)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        var segments = paths.Select(p => p.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var top = segments.All(s => s.Length > 1) && segments.All(s => s[0] == segments[0][0]) ? 1 : 0;
        if (segments.Any(s => s[..^1].Any(DiscFolder().IsMatch)))
        {
            return null; // one book across discs
        }

        string NameKey(string path) => BookFolder(path) + "/" + TitleNormalizer.Normalize(Clean(System.IO.Path.GetFileNameWithoutExtension(path), authors));
        List<FileGroup> ByName(IEnumerable<string> files) => files
            .GroupBy(NameKey, StringComparer.Ordinal)
            .Select(g => new FileGroup(g.Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal).First(), g.ToList()))
            .ToList();

        var folders = segments.All(s => s.Length > top + 1)
            ? segments
                .Select((s, i) => (Folder: string.Join('/', s[..(top + 1)]), Name: s[top], Path: paths[i]))
                .GroupBy(x => x.Folder, StringComparer.Ordinal)
                .ToList()
            : null;

        // A folder of several books, such as a comic series' issues, means the folders aren't books.
        return folders is not null && folders.All(g => g.Select(x => NameKey(x.Path)).Distinct(StringComparer.Ordinal).Count() == 1)
            ? folders.Select(g => new FileGroup(g.First().Name, g.Select(x => x.Path).ToList())).ToList()
            : ByName(paths);
    }

    /// <summary>The folder a file's book is in: its own, or for a folder of variant covers, the one above.</summary>
    private static string BookFolder(string path)
    {
        var folder = System.IO.Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        return VariantCoversFolder().IsMatch(System.IO.Path.GetFileName(folder)) ? System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/') ?? "" : folder;
    }

    /// <summary>
    /// A series with the volume its folder names, when the folder's name is the series' start and a volume such as
    /// <c>v2</c>: <c>Daredevil</c> in <c>Daredevil v2 ½-119+500-512 &amp;Extras</c> is <c>Daredevil v2</c>, and
    /// <c>Daredevil Annual</c> there <c>Daredevil v2 Annual</c>. Otherwise the series as it is.
    /// </summary>
    private static string WithFolderVolume(string series, string path)
    {
        var folder = System.IO.Path.GetFileName(BookFolder(path));
        if (FolderVolume().Match(folder) is not { Success: true } m || TrailingVolume().IsMatch(series) || series.Contains(m.Groups["volume"].Value, StringComparison.OrdinalIgnoreCase))
        {
            return series;
        }

        var start = m.Groups["start"].Value;
        return series.StartsWith(start, StringComparison.OrdinalIgnoreCase) && (series.Length == start.Length || series[start.Length] == ' ')
            ? $"{series[..start.Length]} {m.Groups["volume"].Value}{series[start.Length..]}"
            : series;
    }

    /// <summary>The books with their covers: an image in a book's own folder, <c>cover.*</c> first, else its only one.</summary>
    private static List<CollectionBook> WithCovers(IReadOnlyList<CollectionBook> books, IReadOnlyList<TorrentFileEntry> files)
    {
        var images = files.Select(f => f.Path).Where(p => ImageExtensions.Contains(System.IO.Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToList();
        return books.Select(book =>
        {
            var folder = System.IO.Path.GetDirectoryName(book.Files[0]);
            if (string.IsNullOrEmpty(folder) || books.Count(b => System.IO.Path.GetDirectoryName(b.Files[0]) == folder) > 1)
            {
                return book; // books side by side in one folder: an image there isn't one book's
            }

            var own = images.Where(i => System.IO.Path.GetDirectoryName(i) == folder).ToList();
            var cover = own.FirstOrDefault(i => System.IO.Path.GetFileNameWithoutExtension(i).Equals("cover", StringComparison.OrdinalIgnoreCase))
                ?? (own.Count == 1 ? own[0] : null);
            return book with { CoverFile = cover };
        }).ToList();
    }

    /// <summary>
    /// A folder or file name without what isn't the title: trailing groups in brackets, such as a Calibre id <c>(1019)</c>,
    /// a year <c>(2014)</c> or release tags <c>(Digital) (Zone-Empire)</c>, and a trailing <c> - author</c>.
    /// </summary>
    private static string Clean(string name, IReadOnlyList<string> authors)
    {
        var underscored = !name.Contains(' ', StringComparison.Ordinal) && name.Contains('_', StringComparison.Ordinal);
        var cleaned = Spaces().Replace(TrailingBracketGroups().Replace(underscored ? name.Replace('_', ' ') : name, ""), " ").Trim();
        if (underscored && UnderscoredIssue().Match(cleaned) is { Success: true } issue)
        {
            cleaned = issue.Value; // what followed the number was in brackets, now lost to underscores
        }
        else if (!underscored && IssueBeforeBrackets().Match(cleaned) is { Success: true } tagged)
        {
            cleaned = tagged.Groups["issue"].Value; // tags in brackets, then more: "Daredevil v1 051 (1969) marvel apr- 36p c2c"
        }

        foreach (var author in authors)
        {
            var suffix = " - " + author;
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length].Trim();
            }
        }

        return cleaned.Length == 0 ? name : cleaned;
    }

    /// <summary>
    /// The torrent's title as the series' name, when it names the same series better than the file names do: every word
    /// of <paramref name="series"/> from its names, apart from a trailing volume such as <c>Vol 1</c>, is in the title
    /// (ignoring "and", "&amp;", "the", "of" and "a"), and the words it adds name neither a collection, an author nor years.
    /// <c>Black White Vol 1</c> in <c>Batman: Black and White</c> becomes <c>Batman: Black and White Vol 1</c>; <c>Doonesbury</c>
    /// in <c>Complete Doonesbury - G. B. Trudeau</c> stays. Otherwise <paramref name="series"/> as it is.
    /// </summary>
    private static string BetterSeriesName(string series, TorrentMetadata collection)
    {
        var volume = TrailingVolume().Match(series);
        var bare = volume.Success ? series[..volume.Index].Trim() : series;
        var title = Clean(collection.Title, collection.Authors);
        var titleWords = Words(title);
        var seriesWords = Words(bare);
        var extra = titleWords.Except(seriesWords).ToHashSet(StringComparer.Ordinal);
        var authorWords = collection.Authors.SelectMany(Words).ToHashSet(StringComparer.Ordinal);
        return seriesWords.Count > 0 && seriesWords.IsSubsetOf(titleWords) && !extra.Overlaps(CollectionWords) && !extra.Overlaps(authorWords)
            && !extra.Any(w => w.All(char.IsAsciiDigit))
            ? (volume.Success ? $"{title} {volume.Value.Trim()}" : title)
            : series;
    }

    /// <summary>Words that say a title is the collection's, not a series' name: "Complete Doonesbury", "Daredevil Collection".</summary>
    private static readonly HashSet<string> CollectionWords = new(
        ["collection", "collected", "complete", "omnibus", "set", "box", "boxed", "bundle", "pack", "library", "anthology", "comics", "series"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> MinorWords = new(["and", "the", "of", "a"], StringComparer.Ordinal);

    /// <summary>The words of a name, lower-cased, without the minor ones.</summary>
    private static HashSet<string> Words(string name) =>
        WordPattern().Matches(TextFolding.Fold(name).ToLowerInvariant()).Select(m => m.Value).Where(w => !MinorWords.Contains(w)).ToHashSet(StringComparer.Ordinal);

    /// <summary>A name such as <c>04 - Mort</c>: its number and the title after it.</summary>
    private static (decimal Number, string Title)? LeadingNumber(string name) =>
        LeadingNumberPattern().Match(name) is { Success: true } m && Parse(m.Groups["number"].Value) is { } number
            ? (number, m.Groups["title"].Value.Trim())
            : null;

    /// <summary>
    /// A name such as <c>Ultimate Spider-Man 033</c>, <c>Ultimate Spider-Man 000.5</c>, <c>Ultimate Six 05 - The
    /// Ultimates</c> or <c>Running the Kingdom v01</c> (also <c>Vol. 1</c>, <c>Volume 1</c>, <c>#1</c>): its series and number.
    /// A magazine's <c>Popular Mechanics 10 1927</c> is numbered by its date, 1927.10, so its issues sort by it.
    /// </summary>
    private static (string Series, decimal Number)? NumberedName(string name) =>
        DateIssuePattern().Match(name) is { Success: true } date
            ? (date.Groups["series"].Value.Trim(), decimal.Parse($"{date.Groups["year"].Value}.{date.Groups["month"].Value.PadLeft(2, '0')}", CultureInfo.InvariantCulture))
            : (MarkedNumberPattern().Match(name) is { Success: true } marked ? marked : TrailingNumberPattern().Match(name)) is { Success: true } m
        && Parse(m.Groups["number"].Value) is { } number
            ? (m.Groups["series"].Value.Trim(), number)
            : null;

    /// <summary>A numbered list in a description, such as <c>4. Mort</c> or <c>#4 - Mort</c>, by normalized title.</summary>
    private static List<(string Key, decimal Number)> NumberedList(string? description) =>
        description is null
            ? []
            : ListLine().Matches(description)
                .Select(m => (Key: TitleNormalizer.Normalize(m.Groups["title"].Value), Number: Parse(m.Groups["number"].Value)))
                .Where(x => x.Key.Length > 0 && x.Number is not null)
                .Select(x => (x.Key, x.Number!.Value))
                .ToList();

    /// <summary>The number the description's list gives a title: by its key, or by the start of one shortened in either.</summary>
    private static decimal? FromList(List<(string Key, decimal Number)> listed, string title)
    {
        var key = TitleNormalizer.Normalize(title);
        if (key.Length == 0)
        {
            return null;
        }

        var match = listed.FirstOrDefault(l => l.Key == key);
        if (match.Key is null)
        {
            match = listed.FirstOrDefault(l => l.Key.StartsWith(key, StringComparison.Ordinal) || key.StartsWith(l.Key, StringComparison.Ordinal));
        }

        return match.Key is null ? null : match.Number;
    }

    private static bool SameName(string a, string b) => TitleNormalizer.Normalize(a) == TitleNormalizer.Normalize(b);

    private static decimal? Parse(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string Extension(string path) => System.IO.Path.GetExtension(path).TrimStart('.');

    [GeneratedRegex(@"(\s*(\([^()]*\)|\[[^\[\]]*\]))+\s*$")]
    private static partial Regex TrailingBracketGroups();

    [GeneratedRegex(@"^(?<number>\d{1,4}(\.\d+)?)\s*[-.:)_]\s*(?<title>\S.*)$")]
    private static partial Regex LeadingNumberPattern();

    // A number after a marker (v, vol., volume, #): the series may then end with a digit, as in "Black White Vol 1 #01".
    // A year may follow it ("Shop Notes.Vol.13.1917").
    [GeneratedRegex(@"^(?<series>.*?[^\s#])\s*(?:#|\b(?:v|vol|volume)\.?)\s*(?<number>\d{1,4}(\.\d{1,2}(?!\d))?)(?:[\s._-]+(?:18|19|20)\d\d)?(\s+-\s+\S.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MarkedNumberPattern();

    // A number without a marker: the series ends with neither a dot nor a #, so 000.5 is all number, and is the shortest
    // that leaves a number at the end, so "Ultimates 2 001" is issue 1 of "Ultimates 2"; a title may follow the number
    // after " - ". A hyphen may stand for the space ("Doonesbury-1970"), though not after a digit, so 1-40 is no number.
    [GeneratedRegex(@"^(?<series>.*?[^.\s#])(?:\s+|(?<!\d)\s*-\s*)(?<number>\d{1,4}(\.\d+)?)(\s+-\s+\S.*)?$")]
    private static partial Regex TrailingNumberPattern();

    // An underscored name's series and number, as in "Umbrella_Academy_-_Dallas_002__2008___Steam-DCP_" (brackets
    // replaced by underscores too), or month and year, as in "Popular_Mechanics_10_1927"; a title after " - " is kept.
    [GeneratedRegex(@"^.*?[^.\s#]\s+(?:(?:0?[1-9]|1[0-2])\s+(?:18|19|20)\d\d|\d{1,4}(\.\d+)?)(?=\s|$)(\s+-\s+\S.*)?")]
    private static partial Regex UnderscoredIssue();

    // A magazine's issue by month and year: "Popular Mechanics 10 1927", "Popular Mechanics-07-1961".
    [GeneratedRegex(@"^(?<series>.*?[^\d\s._-])[\s_-]+(?<month>0?[1-9]|1[0-2])[\s_.-]+(?<year>(?:18|19|20)\d\d)$")]
    private static partial Regex DateIssuePattern();

    [GeneratedRegex(@"^(?<issue>.*?\S\s+\d{1,4}(\.\d+)?)\s*[\(\[]")]
    private static partial Regex IssueBeforeBrackets();

    [GeneratedRegex(@"^(variant\s*covers?|variants?|covers?)$", RegexOptions.IgnoreCase)]
    private static partial Regex VariantCoversFolder();

    [GeneratedRegex(@"^(?<start>\S.*?)\s+(?<volume>(?:v|vol\.?\s*|volume\s*)\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FolderVolume();

    [GeneratedRegex(@"\s*\b(?:v|vol|volume)\.?\s*\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVolume();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^\s*#?(?<number>\d{1,4}(\.\d+)?)\s*[-.:)]?\s+(?<title>\S[^\r\n]*?)\s*$", RegexOptions.Multiline)]
    private static partial Regex ListLine();

    [GeneratedRegex(@"^(disc|disk|cd)\s*\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscFolder();
}
