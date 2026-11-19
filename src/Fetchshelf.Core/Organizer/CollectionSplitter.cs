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
        var groups = Group(books.Select(f => f.Path).ToList());
        if (groups is null || groups.Count < 2)
        {
            return null;
        }

        var range = collection.Series.FirstOrDefault(s => s.Entries.Any(e => e.Kind == SeriesEntryKind.Range && e.LastNumber > e.FirstNumber));
        var numbered = groups.ToDictionary(g => g.Name, g => NumberedName(Clean(g.Name, collection.Authors)));
        var largestNamedSeries = numbered.Values.OfType<(string Series, decimal Number)>()
            .GroupBy(n => TitleNormalizer.Normalize(n.Series))
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Max();
        if (range is null && largestNamedSeries < MinimumNumberedBooks)
        {
            return null;
        }

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
                    : leading?.Number ?? numbered[group.Name]?.Number ?? FromList(listed, title);
            }
            else if (numbered[group.Name] is var (series, issue))
            {
                // The torrent's title may name the series better than its file names do.
                var better = BetterSeriesName(series, collection);
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

        return WithCovers(result, files);
    }

    /// <summary>Whether a media type is on the ebook side, the one split.</summary>
    public static bool IsEbookSide(MediaType mediaType) =>
        mediaType is MediaType.Ebook or MediaType.ComicBook or MediaType.Manga or MediaType.PeriodicalEbook;

    private sealed record FileGroup(string Name, List<string> Files);

    /// <summary>
    /// The books: one per sub-folder below the torrent's own folder when every file sits in one; else one per file name,
    /// whatever its format. Null when nothing can be told apart, or a disc folder says it is one book across discs.
    /// </summary>
    private static List<FileGroup>? Group(List<string> paths)
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

        if (segments.All(s => s.Length > top + 1))
        {
            return segments
                .Select((s, i) => (Folder: string.Join('/', s[..(top + 1)]), Name: s[top], Path: paths[i]))
                .GroupBy(x => x.Folder, StringComparer.Ordinal)
                .Select(g => new FileGroup(g.First().Name, g.Select(x => x.Path).ToList()))
                .ToList();
        }

        return paths
            .GroupBy(p => System.IO.Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FileGroup(g.Key, g.ToList()))
            .ToList();
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
        var cleaned = Spaces().Replace(TrailingBracketGroups().Replace(name, ""), " ").Trim();
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
    /// (ignoring "and", "&amp;", "the", "of" and "a"). <c>Black White Vol 1</c> in <c>Batman: Black and White</c> becomes
    /// <c>Batman: Black and White Vol 1</c>. Otherwise <paramref name="series"/> as it is.
    /// </summary>
    private static string BetterSeriesName(string series, TorrentMetadata collection)
    {
        var volume = TrailingVolume().Match(series);
        var bare = volume.Success ? series[..volume.Index].Trim() : series;
        var title = Clean(collection.Title, collection.Authors);
        var titleWords = Words(title);
        var seriesWords = Words(bare);
        return seriesWords.Count > 0 && seriesWords.IsSubsetOf(titleWords) && !SameName(bare, title)
            ? (volume.Success ? $"{title} {volume.Value.Trim()}" : title)
            : series;
    }

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
    /// </summary>
    private static (string Series, decimal Number)? NumberedName(string name) =>
        (MarkedNumberPattern().Match(name) is { Success: true } marked ? marked : TrailingNumberPattern().Match(name)) is { Success: true } m
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
    [GeneratedRegex(@"^(?<series>.*?[^\s#])\s*(?:#|\b(?:v|vol|volume)\.?)\s*(?<number>\d{1,4}(\.\d+)?)(\s+-\s+\S.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MarkedNumberPattern();

    // A number without a marker: the series ends with neither a digit, a dot nor a #, so 000.5 is all number; a title may
    // follow the number after " - ".
    [GeneratedRegex(@"^(?<series>.*?[^\d.\s#])\s+(?<number>\d{1,4}(\.\d+)?)(\s+-\s+\S.*)?$")]
    private static partial Regex TrailingNumberPattern();

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
