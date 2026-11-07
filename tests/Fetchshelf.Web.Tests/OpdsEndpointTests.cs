using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The OPDS catalog under /opds, served from a real library folder.</summary>
public sealed class OpdsEndpointTests : IDisposable
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private readonly string _library = Directory.CreateTempSubdirectory("fetchshelf-opds-library-").FullName;

    public void Dispose() => Directory.Delete(_library, recursive: true);

    private string Config(bool opds = true) => $"""
        mam_id = ""

        [[library]]
        name = "Ebooks"
        category = "ebooks"
        library_dir = "{_library}"
        {(opds ? "\n[opds]\n" : string.Empty)}
        """;

    /// <summary>Puts a book's file in the library folder and records the torrent the way the Link job would.</summary>
    private async Task SeedAsync(
        FetchshelfWebAppFixture fixture, string hash, string title, string author = "Ann Leckie", string extension = "epub",
        string? storedRelativePath = null, Series? series = null, DateTimeOffset? createdAt = null)
    {
        var folder = Path.Combine(_library, author, title);
        Directory.CreateDirectory(folder);
        var fileName = $"{title}.{extension}";
        if (extension == "epub")
        {
            WriteEpub(Path.Combine(folder, fileName));
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(folder, fileName), "%PDF-1.4 test");
        }

        var metadata = OrganizerTestSupport.BuildMetadata(title, [author]) with
        {
            MediaType = MediaType.Ebook,
            Series = series is null ? [] : [series],
        };
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = hash,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(title),
            Title = title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            LibraryPath = folder,
            ChosenEbookExtension = extension,
            LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new(storedRelativePath ?? fileName, new FileInfo(Path.Combine(folder, fileName)).Length) }),
        });
        await dbContext.SaveChangesAsync();
    }

    private static void WriteEpub(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, byte[] bytes)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }

        Add("META-INF/container.xml", Encoding.UTF8.GetBytes(
            """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>"""));
        Add("content.opf", Encoding.UTF8.GetBytes(
            """<package xmlns="http://www.idpf.org/2007/opf"><manifest><item id="c" href="cover.jpg" media-type="image/jpeg" properties="cover-image"/></manifest></package>"""));
        Add("cover.jpg", Jpeg);
    }

    /// <summary>The feed, and its OPDS kind from the content type (navigation or acquisition).</summary>
    private static async Task<(XElement Feed, string Kind)> GetFeedAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contentType = response.Content.Headers.ContentType!;
        Assert.Equal("application/atom+xml", contentType.MediaType);
        Assert.Equal("opds-catalog", contentType.Parameters.Single(p => p.Name == "profile").Value);
        var kind = contentType.Parameters.Single(p => p.Name == "kind").Value!;
        return (XDocument.Parse(await response.Content.ReadAsStringAsync()).Root!, kind);
    }

    private static List<string> Titles(XElement feed) => feed.Elements(Atom + "entry").Select(entry => (string)entry.Element(Atom + "title")!).ToList();

    private static string Link(XElement element, string rel) =>
        (string)element.Elements(Atom + "link").Single(link => (string?)link.Attribute("rel") == rel).Attribute("href")!;

    [Fact]
    public async Task Opds_WithoutTheSection_IsNotServed()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config(opds: false));
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/opds")).StatusCode);
    }

    [Fact]
    public async Task Root_IsANavigationFeedLinkingTheViews()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "aaa111", "Ancillary Justice");
        using var client = fixture.CreateClient();

        var (feed, kind) = await GetFeedAsync(client, "/opds");

        Assert.Equal("navigation", kind);
        Assert.Equal(["Recently added", "Authors", "Series"], Titles(feed));
        Assert.Equal("/opds/search.xml", Link(feed, "search"));
    }

    [Fact]
    public async Task Recent_ListsBooksWithDownloadAndCoverLinks()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "aaa111", "Ancillary Justice", createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        await SeedAsync(fixture, "bbb222", "Ancillary Sword");
        using var client = fixture.CreateClient();

        var (feed, kind) = await GetFeedAsync(client, "/opds/recent");

        Assert.Equal("acquisition", kind);
        Assert.Equal(["Ancillary Sword", "Ancillary Justice"], Titles(feed));
        var entry = feed.Elements(Atom + "entry").First();
        Assert.Equal("/opds/books/bbb222/files/0", Link(entry, "http://opds-spec.org/acquisition"));
        Assert.Equal("/opds/books/bbb222/cover", Link(entry, "http://opds-spec.org/image"));
    }

    [Fact]
    public async Task AuthorsAndSeries_LeadToTheirBooks()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "aaa111", "Ancillary Sword", series: new Series { Name = "Imperial Radch", Entries = [SeriesEntry.ForNumber(2, "2")] });
        await SeedAsync(fixture, "bbb222", "Ancillary Justice", series: new Series { Name = "Imperial Radch", Entries = [SeriesEntry.ForNumber(1, "1")] });
        await SeedAsync(fixture, "ccc333", "Dune", "Frank Herbert");
        using var client = fixture.CreateClient();

        var (authors, _) = await GetFeedAsync(client, "/opds/authors");
        Assert.Equal(["Ann Leckie", "Frank Herbert"], Titles(authors));
        var authorHref = Link(authors.Elements(Atom + "entry").First(), "subsection");
        Assert.Equal(["Ancillary Justice", "Ancillary Sword"], Titles((await GetFeedAsync(client, authorHref)).Feed));

        var (series, _) = await GetFeedAsync(client, "/opds/series");
        Assert.Equal(["Imperial Radch"], Titles(series));
        Assert.Equal(["Ancillary Justice", "Ancillary Sword"], Titles((await GetFeedAsync(client, Link(series.Element(Atom + "entry")!, "subsection"))).Feed));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/opds/author?name=Nobody")).StatusCode);
    }

    [Fact]
    public async Task Search_FindsByTitleOrAuthor_AndTheDescriptionHasAFullTemplate()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "aaa111", "Ancillary Justice");
        await SeedAsync(fixture, "ccc333", "Dune", "Frank Herbert");
        using var client = fixture.CreateClient();

        Assert.Equal(["Dune"], Titles((await GetFeedAsync(client, "/opds/search?q=herbert")).Feed));

        var description = XDocument.Parse(await client.GetStringAsync("/opds/search.xml")).Root!;
        var template = (string)description.Descendants().Single(e => e.Name.LocalName == "Url").Attribute("template")!;
        Assert.Equal("http://localhost/opds/search?q={searchTerms}", template);
    }

    [Fact]
    public async Task Download_ServesTheFileWithItsTypeAndANiceName_AndSupportsRanges()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "pdf111", "A Memory Called Empire", "Arkady Martine", "pdf");
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/opds/books/pdf111/files/0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("A Memory Called Empire - Arkady Martine.pdf", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("%PDF-1.4 test", await response.Content.ReadAsStringAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/opds/books/pdf111/files/0");
        request.Headers.Range = new RangeHeaderValue(0, 3);
        using var partial = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("%PDF", await partial.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/opds/books/unknown/files/0")]
    [InlineData("/opds/books/pdf111/files/1")]
    [InlineData("/opds/books/pdf111/files/-1")]
    [InlineData("/opds/books/pdf111/cover")]
    public async Task UnknownBookFileOrCover_IsNotFound(string url)
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "pdf111", "A Memory Called Empire", "Arkady Martine", "pdf");
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Download_StoredPathLeavingTheBookFolder_IsRefused()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await File.WriteAllTextAsync(Path.Combine(_library, "secret.epub"), "not for readers");
        await SeedAsync(fixture, "evil11", "Book", storedRelativePath: "../../secret.epub");
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/opds/books/evil11/files/0")).StatusCode);
    }

    [Fact]
    public async Task Cover_IsReadFromTheEpub()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        await SeedAsync(fixture, "aaa111", "Ancillary Justice");
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/opds/books/aaa111/cover");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Jpeg, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Recent_PagesOfFifty()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        for (var i = 0; i < 51; i++)
        {
            await SeedAsync(fixture, $"h{i:D3}", $"Book {i:D2}", extension: "pdf", createdAt: DateTimeOffset.UtcNow.AddMinutes(-i));
        }

        using var client = fixture.CreateClient();

        var (first, _) = await GetFeedAsync(client, "/opds/recent");
        Assert.Equal(50, Titles(first).Count);
        Assert.Equal("/opds/recent?page=2", Link(first, "next"));
        var (second, _) = await GetFeedAsync(client, Link(first, "next"));
        Assert.Equal(["Book 50"], Titles(second));
        Assert.Equal("/opds/recent?page=1", Link(second, "previous"));
    }

    [Fact]
    public async Task ConfigPage_ShowsTheCatalogAddress()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config());
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/config");

        Assert.Contains("<code id=\"opds-url\">http://localhost/opds</code>", html);
    }
}
