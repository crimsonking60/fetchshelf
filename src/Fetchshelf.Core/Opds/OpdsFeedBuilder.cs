using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Opds;

/// <summary>A link in a navigation feed: a title, a short text, and the feed it leads to.</summary>
public sealed record OpdsNavigationEntry(string Id, string Title, string? Text, string Href, bool LeadsToBooks);

/// <summary>Which page of a list a feed shows, and how to address the others.</summary>
/// <param name="Number">The page shown, from 1.</param>
/// <param name="TotalPages">How many pages the list has, at least 1.</param>
/// <param name="TotalItems">How many items the whole list has.</param>
/// <param name="PageSize">Items per page.</param>
/// <param name="HrefFor">The address of a given page.</param>
public sealed record OpdsPage(int Number, int TotalPages, int TotalItems, int PageSize, Func<int, string> HrefFor)
{
    /// <summary>The page of <paramref name="items"/> asked for, with the page number clamped to the pages there are.</summary>
    public static (IReadOnlyList<T> Items, OpdsPage Page) Of<T>(IReadOnlyList<T> items, int requested, int pageSize, Func<int, string> hrefFor)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)pageSize));
        var number = Math.Clamp(requested, 1, totalPages);
        return (items.Skip((number - 1) * pageSize).Take(pageSize).ToList(), new OpdsPage(number, totalPages, items.Count, pageSize, hrefFor));
    }
}

/// <summary>
/// Builds OPDS 1.2 catalog documents (Atom feeds) and the OpenSearch description. Links are relative to the host,
/// so the feeds work behind whatever host name and port a reader uses.
/// </summary>
public static class OpdsFeedBuilder
{
    public const string NavigationType = "application/atom+xml;profile=opds-catalog;kind=navigation";
    public const string AcquisitionType = "application/atom+xml;profile=opds-catalog;kind=acquisition";
    public const string OpenSearchType = "application/opensearchdescription+xml";
    public const string RootHref = "/opds";
    public const string OpenSearchHref = "/opds/search.xml";

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Dc = "http://purl.org/dc/terms/";
    private static readonly XNamespace Opds = "http://opds-spec.org/2010/catalog";
    private static readonly XNamespace OpenSearch = "http://a9.com/-/spec/opensearch/1.1/";

    /// <summary>The address of a book's file, by its position in <see cref="OpdsBook.Files"/>.</summary>
    public static string FileHref(OpdsBook book, int index) => $"/opds/books/{Uri.EscapeDataString(book.Id)}/files/{index}";

    /// <summary>The address of a book's cover.</summary>
    public static string CoverHref(OpdsBook book) => $"/opds/books/{Uri.EscapeDataString(book.Id)}/cover";

    /// <summary>A feed of links to other feeds, such as the root catalog or the list of authors.</summary>
    public static XDocument Navigation(
        string id, string title, string selfHref, DateTimeOffset updated, IEnumerable<OpdsNavigationEntry> entries, OpdsPage? page = null)
    {
        var feed = Feed(id, title, selfHref, NavigationType, updated, page);
        foreach (var entry in entries)
        {
            feed.Add(new XElement(Atom + "entry",
                new XElement(Atom + "title", entry.Title),
                new XElement(Atom + "id", entry.Id),
                new XElement(Atom + "updated", Timestamp(updated)),
                entry.Text is null ? null : new XElement(Atom + "content", new XAttribute("type", "text"), entry.Text),
                Link("subsection", entry.Href, entry.LeadsToBooks ? AcquisitionType : NavigationType)));
        }

        return new XDocument(feed);
    }

    /// <summary>A feed of books, each with its download links.</summary>
    public static XDocument Acquisition(
        string id, string title, string selfHref, DateTimeOffset updated, IEnumerable<OpdsBook> books, OpdsPage page)
    {
        var feed = Feed(id, title, selfHref, AcquisitionType, updated, page);
        feed.Add(
            new XElement(OpenSearch + "totalResults", page.TotalItems),
            new XElement(OpenSearch + "itemsPerPage", page.PageSize),
            new XElement(OpenSearch + "startIndex", ((page.Number - 1) * page.PageSize) + 1));
        foreach (var book in books)
        {
            feed.Add(BookEntry(book));
        }

        return new XDocument(feed);
    }

    /// <summary>The OpenSearch description readers fetch to learn how to search; <paramref name="searchTemplate"/> holds `{searchTerms}`.</summary>
    public static XDocument OpenSearchDescription(string searchTemplate) =>
        new(new XElement(OpenSearch + "OpenSearchDescription",
            new XElement(OpenSearch + "ShortName", "Fetchshelf"),
            new XElement(OpenSearch + "Description", "Search the books in Fetchshelf's libraries by title, author or series."),
            new XElement(OpenSearch + "InputEncoding", "UTF-8"),
            new XElement(OpenSearch + "OutputEncoding", "UTF-8"),
            new XElement(OpenSearch + "Url",
                new XAttribute("type", AcquisitionType),
                new XAttribute("template", searchTemplate))));

    /// <summary>
    /// The document as UTF-8 text with an XML declaration. Characters XML can't hold, such as a form feed in a MAM
    /// description, are left out rather than failing the whole feed.
    /// </summary>
    public static string Serialize(XDocument document)
    {
        foreach (var text in document.DescendantNodes().OfType<XText>())
        {
            text.Value = XmlSafeText.Clean(text.Value);
        }

        foreach (var attribute in document.Descendants().Attributes())
        {
            attribute.Value = XmlSafeText.Clean(attribute.Value);
        }

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The MIME type a format's files are served with.</summary>
    public static string MediaTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        "epub" => "application/epub+zip",
        "pdf" => "application/pdf",
        "mobi" => "application/x-mobipocket-ebook",
        "azw3" or "azw" => "application/vnd.amazon.ebook",
        "cbz" => "application/vnd.comicbook+zip",
        "cbr" => "application/vnd.comicbook-rar",
        _ => "application/octet-stream",
    };

    private static XElement Feed(string id, string title, string selfHref, string selfType, DateTimeOffset updated, OpdsPage? page)
    {
        var feed = new XElement(Atom + "feed",
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(XNamespace.Xmlns + "opds", Opds),
            new XAttribute(XNamespace.Xmlns + "opensearch", OpenSearch),
            new XElement(Atom + "id", id),
            new XElement(Atom + "title", title),
            new XElement(Atom + "updated", Timestamp(updated)),
            new XElement(Atom + "author", new XElement(Atom + "name", "Fetchshelf")),
            Link("self", selfHref, selfType),
            Link("start", RootHref, NavigationType),
            Link("search", OpenSearchHref, OpenSearchType));

        if (page is { TotalPages: > 1 })
        {
            feed.Add(Link("first", page.HrefFor(1), selfType));
            if (page.Number > 1)
            {
                feed.Add(Link("previous", page.HrefFor(page.Number - 1), selfType));
            }

            if (page.Number < page.TotalPages)
            {
                feed.Add(Link("next", page.HrefFor(page.Number + 1), selfType));
            }

            feed.Add(Link("last", page.HrefFor(page.TotalPages), selfType));
        }

        return feed;
    }

    private static XElement BookEntry(OpdsBook book)
    {
        var entry = new XElement(Atom + "entry",
            new XElement(Atom + "title", book.Title),
            new XElement(Atom + "id", $"urn:fetchshelf:book:{book.Id}"),
            new XElement(Atom + "updated", Timestamp(book.AddedAt)),
            book.Authors.Select(author => new XElement(Atom + "author", new XElement(Atom + "name", author))),
            LanguageCodes.For(book.Language) is { } language ? new XElement(Dc + "language", language) : null,
            book.Genres.Select(genre => new XElement(Atom + "category", new XAttribute("term", genre), new XAttribute("label", genre))));

        // OPDS 1.2 has no series field, so the series go at the top of the description, where every reader shows them.
        var seriesLines = book.Series.Select(series => series.Entry is null ? series.Name : $"{series.Name} #{series.Entry}").ToList();
        var text = string.Join("\n\n", new[] { string.Join("\n", seriesLines), book.Description }.Where(part => !string.IsNullOrEmpty(part)));
        if (text.Length > 0)
        {
            entry.Add(new XElement(Atom + "content", new XAttribute("type", "text"), text));
        }

        if (book.MayHaveCover)
        {
            entry.Add(
                Link("http://opds-spec.org/image", CoverHref(book), type: null),
                Link("http://opds-spec.org/image/thumbnail", CoverHref(book), type: null));
        }

        for (var i = 0; i < book.Files.Count; i++)
        {
            var file = book.Files[i];
            var link = Link("http://opds-spec.org/acquisition", FileHref(book, i), MediaTypeFor(file.Extension));
            link.Add(new XAttribute("length", file.SizeBytes.ToString(CultureInfo.InvariantCulture)));
            if (book.Files.Count > 1)
            {
                link.Add(new XAttribute("title", $"Part {i + 1} of {book.Files.Count}"));
            }

            entry.Add(link);
        }

        return entry;
    }

    private static XElement Link(string rel, string href, string? type) =>
        new(Atom + "link",
            new XAttribute("rel", rel),
            new XAttribute("href", href),
            type is null ? null : new XAttribute("type", type));

    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
