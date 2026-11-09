using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Opds;

/// <summary>A served library: its index in the configuration and the name it is shown with.</summary>
public sealed record OpdsLibrary(int Index, string Name);

/// <summary>A name in a navigation list (an author or a series) and how many books it has.</summary>
public sealed record OpdsGroup(string Name, int BookCount);

/// <summary>
/// The books the OPDS catalog serves, and the views the feeds are built from. Built once and then only read, so it
/// can be shared between requests.
/// </summary>
public sealed class OpdsCatalog(IReadOnlyList<OpdsBook> books, IReadOnlyList<OpdsLibrary> libraries)
{
    private static readonly StringComparer NameOrder = StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true);

    private readonly Dictionary<string, OpdsBook> _byId = books.ToDictionary(book => book.Id, StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, OpdsCatalog> _byLibrary = new();

    public IReadOnlyList<OpdsBook> Books { get; } = books;

    /// <summary>The served libraries that have at least one book, in configuration order.</summary>
    public IReadOnlyList<OpdsLibrary> Libraries { get; } =
        libraries.Where(library => books.Any(book => book.LibraryIndex == library.Index)).ToList();

    public OpdsBook? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Every book, newest first.</summary>
    public IReadOnlyList<OpdsBook> Recent() => Newest(Books);

    /// <summary>Every author, A-Z, with the number of books they wrote or co-wrote.</summary>
    public IReadOnlyList<OpdsGroup> Authors() => Groups(Books.SelectMany(book => book.Authors));

    /// <summary>One author's books, by series and number, then by title.</summary>
    public IReadOnlyList<OpdsBook> ByAuthor(string author) =>
        Books.Where(book => book.Authors.Contains(author, StringComparer.Ordinal))
            .OrderBy(book => book.Series.Count == 0 ? 1 : 0)
            .ThenBy(book => book.Series.Count > 0 ? book.Series[0].Name : string.Empty, NameOrder)
            .ThenBy(book => book.Series.Count > 0 ? book.Series[0].Number ?? decimal.MaxValue : decimal.MaxValue)
            .ThenBy(book => book.Title, NameOrder)
            .ToList();

    /// <summary>Every series, A-Z, with the number of books in it.</summary>
    public IReadOnlyList<OpdsGroup> Series() => Groups(Books.SelectMany(book => book.Series.Select(series => series.Name)));

    /// <summary>One series' books in series order; books without a number come last, by title.</summary>
    public IReadOnlyList<OpdsBook> BySeries(string name) =>
        Books.Select(book => (Book: book, Series: book.Series.FirstOrDefault(series => series.Name == name)))
            .Where(pair => pair.Series is not null)
            .OrderBy(pair => pair.Series!.Number ?? decimal.MaxValue)
            .ThenBy(pair => pair.Book.Title, NameOrder)
            .Select(pair => pair.Book)
            .ToList();

    /// <summary>One library's books, newest first.</summary>
    public IReadOnlyList<OpdsBook> ByLibrary(int index) => Newest(Books.Where(book => book.LibraryIndex == index));

    /// <summary>
    /// The catalog of one served library, with the same views as the whole catalog; null when no served library with
    /// books has that index. Built once per library and kept, like the catalog itself.
    /// </summary>
    public OpdsCatalog? ForLibrary(int index) =>
        Libraries.FirstOrDefault(library => library.Index == index) is { } library
            ? _byLibrary.GetOrAdd(index, _ => new OpdsCatalog(Books.Where(book => book.LibraryIndex == index).ToList(), [library]))
            : null;

    /// <summary>The books with every word of <paramref name="query"/> in their title, an author or a series name, newest first.</summary>
    public IReadOnlyList<OpdsBook> Search(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : Newest(Books.Where(book =>
                TorrentSearchMatcher.MatchesAny([book.Title, .. book.Authors, .. book.Series.Select(series => series.Name)], query)));

    private static List<OpdsBook> Newest(IEnumerable<OpdsBook> books) =>
        books.OrderByDescending(book => book.AddedAt).ThenBy(book => book.Title, NameOrder).ToList();

    private static List<OpdsGroup> Groups(IEnumerable<string> names) =>
        names.Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Select(group => new OpdsGroup(group.Key, group.Count()))
            .OrderBy(group => group.Name, NameOrder)
            .ToList();
}
