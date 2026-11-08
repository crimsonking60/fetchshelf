using System.Xml.Linq;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Opds;

namespace Fetchshelf.Core.Tests;

public class OpdsFeedBuilderTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Dc = "http://purl.org/dc/terms/";
    private static readonly XNamespace OpenSearch = "http://a9.com/-/spec/opensearch/1.1/";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 30, 0, TimeSpan.FromHours(1));

    private static OpdsBook Book(string extension = "epub", int files = 1, string? description = "The first book.", IReadOnlyList<Series>? series = null) =>
        OpdsBooks.From(
            "abc123",
            OpdsCatalogTests.Metadata("The Way of Kings", series: series ?? [OpdsCatalogTests.InSeries("Stormlight Archive", 1, "1")]) with { Description = description },
            "/books/kings", Now, 0, extension,
            Enumerable.Range(1, files).Select(i => ($"part{i}.{extension}", (long)(1000 * i))))!;

    private static string Href(XElement parent, string rel) =>
        (string)parent.Elements(Atom + "link").Single(link => (string?)link.Attribute("rel") == rel).Attribute("href")!;

    private static OpdsPage Page(int number, int totalPages) =>
        new(number, totalPages, totalPages * 50, 50, page => $"/opds/recent?page={page}");

    [Fact]
    public void Navigation_HasTheRequiredFeedElementsAndLinks()
    {
        var feed = OpdsFeedBuilder.Navigation("urn:fetchshelf:opds:root", "Fetchshelf", "/opds", Now,
            [new OpdsNavigationEntry("urn:x:recent", "Recently added", "The newest books", "/opds/recent", LeadsToBooks: true)]).Root!;

        Assert.Equal(Atom + "feed", feed.Name);
        Assert.Equal("urn:fetchshelf:opds:root", (string?)feed.Element(Atom + "id"));
        Assert.Equal("2026-11-07T11:30:00Z", (string?)feed.Element(Atom + "updated"));
        Assert.Equal("/opds", Href(feed, "self"));
        Assert.Equal("/opds", Href(feed, "start"));
        Assert.Equal("/opds/search.xml", Href(feed, "search"));

        var entry = Assert.Single(feed.Elements(Atom + "entry"));
        var subsection = entry.Element(Atom + "link")!;
        Assert.Equal("subsection", (string?)subsection.Attribute("rel"));
        Assert.Equal("/opds/recent", (string?)subsection.Attribute("href"));
        Assert.Equal(OpdsFeedBuilder.AcquisitionType, (string?)subsection.Attribute("type"));
        Assert.Equal("The newest books", (string?)entry.Element(Atom + "content"));
    }

    [Theory]
    [InlineData(1, new[] { "first", "next", "last" })]
    [InlineData(2, new[] { "first", "previous", "next", "last" })]
    [InlineData(3, new[] { "first", "previous", "last" })]
    public void Acquisition_PagingLinksMatchThePage(int number, string[] rels)
    {
        var feed = OpdsFeedBuilder.Acquisition("urn:x", "Recent", "/opds/recent", Now, [], Page(number, 3)).Root!;

        var pagingRels = feed.Elements(Atom + "link").Select(link => (string)link.Attribute("rel")!)
            .Where(rel => rel is "first" or "previous" or "next" or "last");
        Assert.Equal(rels, pagingRels);
        Assert.Equal("/opds/recent?page=3", Href(feed, "last"));
        Assert.Equal(((number - 1) * 50) + 1, (int)feed.Element(OpenSearch + "startIndex")!);
        Assert.Equal(150, (int)feed.Element(OpenSearch + "totalResults")!);
    }

    [Fact]
    public void Acquisition_OnePage_HasNoPagingLinks()
    {
        var feed = OpdsFeedBuilder.Acquisition("urn:x", "Recent", "/opds/recent", Now, [], Page(1, 1)).Root!;

        Assert.DoesNotContain(feed.Elements(Atom + "link"), link => (string?)link.Attribute("rel") is "first" or "next");
    }

    [Fact]
    public void Acquisition_BookEntry_HasMetadataAndDownloadLink()
    {
        var feed = OpdsFeedBuilder.Acquisition("urn:x", "Recent", "/opds/recent", Now, [Book()], Page(1, 1)).Root!;
        var entry = Assert.Single(feed.Elements(Atom + "entry"));

        Assert.Equal("The Way of Kings", (string?)entry.Element(Atom + "title"));
        Assert.Equal("urn:fetchshelf:book:abc123", (string?)entry.Element(Atom + "id"));
        Assert.Equal(["Brandon Sanderson"], entry.Elements(Atom + "author").Select(author => (string)author.Element(Atom + "name")!));
        Assert.Equal("eng", (string?)entry.Element(Dc + "language"));
        Assert.Contains("Fantasy", entry.Elements(Atom + "category").Select(category => (string)category.Attribute("term")!));
        Assert.Equal("Stormlight Archive #1\n\nThe first book.", (string?)entry.Element(Atom + "content"));

        var acquisition = entry.Elements(Atom + "link").Single(link => (string?)link.Attribute("rel") == "http://opds-spec.org/acquisition");
        Assert.Equal("/opds/books/abc123/files/0", (string?)acquisition.Attribute("href"));
        Assert.Equal("application/epub+zip", (string?)acquisition.Attribute("type"));
        Assert.Equal("1000", (string?)acquisition.Attribute("length"));
        Assert.Null(acquisition.Attribute("title"));
        Assert.Equal("/opds/books/abc123/cover", Href(entry, "http://opds-spec.org/image"));
        Assert.Equal("/opds/books/abc123/cover", Href(entry, "http://opds-spec.org/image/thumbnail"));
    }

    [Fact]
    public void Acquisition_PdfInParts_HasALinkPerPartAndNoCover()
    {
        var entry = OpdsFeedBuilder.Acquisition("urn:x", "Recent", "/opds/recent", Now, [Book("pdf", files: 2)], Page(1, 1)).Root!
            .Element(Atom + "entry")!;

        var acquisitions = entry.Elements(Atom + "link").Where(link => (string?)link.Attribute("rel") == "http://opds-spec.org/acquisition").ToList();
        Assert.Equal(["Part 1 of 2", "Part 2 of 2"], acquisitions.Select(link => (string)link.Attribute("title")!));
        Assert.All(acquisitions, link => Assert.Equal("application/pdf", (string?)link.Attribute("type")));
        Assert.DoesNotContain(entry.Elements(Atom + "link"), link => ((string?)link.Attribute("rel"))!.StartsWith("http://opds-spec.org/image", StringComparison.Ordinal));
    }

    [Fact]
    public void Acquisition_NoSeriesOrDescription_HasNoContent()
    {
        var entry = OpdsFeedBuilder.Acquisition("urn:x", "Recent", "/opds/recent", Now, [Book(description: null, series: [])], Page(1, 1)).Root!
            .Element(Atom + "entry")!;

        Assert.Null(entry.Element(Atom + "content"));
    }

    [Fact]
    public void OpenSearchDescription_PointsAtTheSearchFeed()
    {
        var root = OpdsFeedBuilder.OpenSearchDescription("http://nas:3157/opds/search?q={searchTerms}").Root!;
        var url = root.Element(OpenSearch + "Url")!;

        Assert.Equal("http://nas:3157/opds/search?q={searchTerms}", (string?)url.Attribute("template"));
        Assert.Equal(OpdsFeedBuilder.AcquisitionType, (string?)url.Attribute("type"));
    }

    [Fact]
    public void Serialize_WritesUtf8WithADeclaration()
    {
        var xml = OpdsFeedBuilder.Serialize(OpdsFeedBuilder.Navigation("urn:x", "Søk & finn", "/opds", Now, []));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Contains("<title>Søk &amp; finn</title>", xml);
    }

    [Theory]
    [InlineData("epub", "application/epub+zip")]
    [InlineData("PDF", "application/pdf")]
    [InlineData("azw3", "application/vnd.amazon.ebook")]
    [InlineData("cbz", "application/vnd.comicbook+zip")]
    [InlineData("djvu", "application/octet-stream")]
    public void MediaTypeFor_KnownAndUnknownFormats(string extension, string mediaType) =>
        Assert.Equal(mediaType, OpdsFeedBuilder.MediaTypeFor(extension));
}
