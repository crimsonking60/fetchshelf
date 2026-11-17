using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Lists;

/// <summary>How a list file was read.</summary>
public enum ListFileFormat
{
    /// <summary>A table whose first line names its columns, one of them <c>Title</c>.</summary>
    Csv,

    /// <summary>One book per line: <c>Title by Author</c>, <c>Title - Author</c>, or just the title.</summary>
    Text,
}

/// <summary>A line of a list file that couldn't be read, and why.</summary>
public sealed record ListFileProblem(int Line, string Reason);

/// <summary>What a list file holds.</summary>
/// <param name="Books">The books, in the order of the file, each once.</param>
/// <param name="Problems">The lines that couldn't be read.</param>
/// <param name="HasShelfColumn">Whether the file has a column a `shelf` can be matched against.</param>
public sealed record ListFileContents(IReadOnlyList<ListBook> Books, IReadOnlyList<ListFileProblem> Problems, ListFileFormat Format, bool HasShelfColumn);

/// <summary>
/// Reads a list of wanted books from a file's text: a CSV export from Goodreads or StoryGraph, a hand-made table, or
/// plain text with one book per line. The format is told from the content: a first line with a <c>Title</c> column is a
/// table.
/// </summary>
/// <remarks>
/// A book's id is its ISBN when it has one, else its title key and first author's key, so the same book stays the
/// same list item when the file is exported again or reordered. Two rows with the same id are one book. A row that
/// can't be read is left out and reported with its line number.
/// </remarks>
public static partial class ListFileReader
{
    private static readonly string[] ShelfColumns = ["exclusive shelf", "read status", "shelf"];

    /// <param name="text">The file's text.</param>
    /// <param name="shelf">Keeps only the rows on this shelf, for a file with a shelf column; null keeps every row.</param>
    public static ListFileContents Read(string text, string? shelf = null)
    {
        text = text.TrimStart('﻿');
        return HeaderDelimiter(text) is { } delimiter ? ReadCsv(text, delimiter, shelf) : ReadText(text);
    }

    /// <summary>The delimiter of a table whose first line has a <c>Title</c> column; null when the text isn't one.</summary>
    private static char? HeaderDelimiter(string text)
    {
        var firstLine = text.Split('\n').Select(line => line.TrimEnd('\r')).FirstOrDefault(line => line.Trim().Length > 0);
        if (firstLine is null)
        {
            return null;
        }

        foreach (var delimiter in new[] { ',', ';' })
        {
            var header = Records(firstLine, delimiter).FirstOrDefault();
            if (header.Cells?.Any(cell => Column(cell) == "title") == true)
            {
                return delimiter;
            }
        }

        return null;
    }

    private static ListFileContents ReadCsv(string text, char delimiter, string? shelf)
    {
        var books = new Builder();
        var problems = new List<ListFileProblem>();
        Dictionary<string, int>? columns = null;
        foreach (var (line, cells) in Records(text, delimiter))
        {
            if (cells is null)
            {
                problems.Add(new ListFileProblem(line, "a quote is not closed"));
                continue;
            }

            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (columns is null)
            {
                columns = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < cells.Count; i++)
                {
                    columns.TryAdd(Column(cells[i]), i);
                }

                continue;
            }

            string? Cell(params string[] names) =>
                names.Select(name => columns.TryGetValue(name, out var i) && i < cells.Count ? Clean(cells[i]) : null)
                    .FirstOrDefault(value => !string.IsNullOrEmpty(value));

            if (shelf is not null && ShelfColumns.FirstOrDefault(columns.ContainsKey) is { } shelfColumn
                && !string.Equals(Cell(shelfColumn), shelf.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = Cell("title");
            if (title is null)
            {
                problems.Add(new ListFileProblem(line, "it has no title"));
                continue;
            }

            var authors = new List<string>();
            if (Cell("author") is { } author)
            {
                authors.AddRange(Split(author, ';', '&'));
            }

            if (Cell("additional authors") is { } additional)
            {
                authors.AddRange(Split(additional, ','));
            }

            if (authors.Count == 0 && Cell("authors") is { } listed)
            {
                authors.AddRange(Split(listed, ',', ';', '&'));
            }

            var seriesName = Cell("series");
            var seriesNumber = seriesName is null ? null : Number(Cell("series number", "number"));
            if (seriesName is null)
            {
                (title, seriesName, seriesNumber) = SplitSeries(title);
            }

            var identifiers = new[] { Cell("isbn13"), Cell("isbn"), Cell("isbn/uid") }.OfType<string>().ToList();
            books.Add(title, authors, seriesName, seriesNumber, identifiers);
        }

        return new ListFileContents(books.Books, problems, ListFileFormat.Csv, columns?.Keys.Any(ShelfColumns.Contains) == true);
    }

    private static ListFileContents ReadText(string text)
    {
        var books = new Builder();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var (title, author) = SplitAuthor(line);
            var (bookTitle, seriesName, seriesNumber) = SplitSeries(title);
            books.Add(bookTitle, author is null ? [] : Split(author, ';', '&'), seriesName, seriesNumber, []);
        }

        return new ListFileContents(books.Books, [], ListFileFormat.Text, HasShelfColumn: false);
    }

    /// <summary>"Title by Author" or "Title - Author", split at the last such separator; a line without one is a title.</summary>
    private static (string Title, string? Author) SplitAuthor(string line)
    {
        foreach (var separator in new[] { " by ", " - " })
        {
            var at = line.LastIndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (at > 0 && line[(at + separator.Length)..].Trim() is { Length: > 0 } author)
            {
                return (line[..at].Trim(), author);
            }
        }

        return (line, null);
    }

    /// <summary>Goodreads' "Title (Series Name, #3)": the series taken off the title. A title without one is kept as it is.</summary>
    private static (string Title, string? SeriesName, double? SeriesNumber) SplitSeries(string title)
    {
        var match = TitleWithSeries().Match(title);
        if (!match.Success)
        {
            return (title, null, null);
        }

        var series = SeriesParser.Parse(match.Groups["series"].Value.Trim(), match.Groups["entries"].Value);
        var number = series.Entries.Count > 0 ? (double?)series.Entries[0].FirstNumber : null;
        return (match.Groups["title"].Value.Trim(), series.Name, number);
    }

    [GeneratedRegex(@"^(?<title>.+?)\s*\((?<series>[^()#]+?),?\s*#(?<entries>[^()]+)\)$")]
    private static partial Regex TitleWithSeries();

    private static double? Number(string? value) =>
        double.TryParse(value?.TrimStart('#'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static List<string> Split(string value, params char[] separators) =>
        value.Split(separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string Column(string header) => header.Trim().ToLowerInvariant();

    /// <summary>A cell's value: trimmed, and Goodreads' <c>="0441172717"</c>, which keeps a spreadsheet from reading a number, unwrapped.</summary>
    private static string Clean(string cell)
    {
        var value = cell.Trim();
        if (value.StartsWith("=\"", StringComparison.Ordinal) && value.EndsWith('"'))
        {
            value = value[2..^1].Trim();
        }

        return value;
    }

    /// <summary>
    /// The records of a table, each with the line it starts on. A quoted field may hold the delimiter, doubled quotes
    /// and line breaks. A record whose quote is never closed comes back without cells, and reading goes on from the line
    /// after the one it started on.
    /// </summary>
    private static IEnumerable<(int Line, List<string>? Cells)> Records(string text, char delimiter)
    {
        var position = 0;
        var line = 1;
        while (position < text.Length)
        {
            var (startPosition, startLine) = (position, line);
            var cells = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var unclosed = false;
            while (true)
            {
                if (position >= text.Length)
                {
                    unclosed = inQuotes;
                    break;
                }

                var c = text[position++];
                if (inQuotes)
                {
                    if (c == '"' && position < text.Length && text[position] == '"')
                    {
                        field.Append('"');
                        position++;
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        line += c == '\n' ? 1 : 0;
                        field.Append(c);
                    }
                }
                else if (c == '"' && field.ToString().Trim().Length == 0)
                {
                    field.Clear();
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    cells.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n')
                {
                    line++;
                    break;
                }
                else if (c != '\r')
                {
                    field.Append(c);
                }
            }

            if (unclosed)
            {
                yield return (startLine, null);
                var nextLine = text.IndexOf('\n', startPosition);
                position = nextLine < 0 ? text.Length : nextLine + 1;
                line = startLine + 1;
                continue;
            }

            cells.Add(field.ToString());
            yield return (startLine, cells);
        }
    }

    /// <summary>Collects the books in file order, each id once.</summary>
    private sealed class Builder
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public List<ListBook> Books { get; } = [];

        public void Add(string title, IReadOnlyList<string> authors, string? seriesName, double? seriesNumber, IReadOnlyList<string> identifiers)
        {
            var id = IdOf(title, authors, identifiers);
            if (_ids.Add(id))
            {
                Books.Add(new ListBook(id, title, authors) { SeriesName = seriesName, SeriesNumber = seriesNumber, Identifiers = identifiers });
            }
        }
    }

    /// <summary>
    /// A book's id: <c>isbn:</c> and its ISBN-13 when it has a valid ISBN, else <c>title:</c> with its title key and first
    /// author's key. It holds no spaces, as an error's key is the list id and the book id joined by one.
    /// </summary>
    public static string IdOf(string title, IReadOnlyList<string> authors, IReadOnlyList<string> identifiers)
    {
        // ISBNs only: an ASIN starts with a letter.
        var keys = identifiers.SelectMany(BookIdentifiers.Keys).Where(key => char.IsAsciiDigit(key[0])).ToList();
        if ((keys.FirstOrDefault(key => key.Length == 13) ?? keys.FirstOrDefault()) is { } isbn)
        {
            return $"isbn:{isbn}";
        }

        var author = authors.Count > 0 ? PersonNameKey.Of(authors[0]) : string.Empty;
        return $"title:{TitleNormalizer.Normalize(title).Replace(' ', '-')}|{author}";
    }
}
