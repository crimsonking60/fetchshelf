using System.IO.Compression;
using System.Text;
using Fetchshelf.Core.Opds;
using Fetchshelf.Data.Opds;

namespace Fetchshelf.Data.Tests;

public sealed class BookCoverExtractorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-cover-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    internal static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 5, 6];

    /// <summary>Writes a zip with the given entries to a new file in the test folder and returns its path.</summary>
    internal static string WriteZip(string folder, string fileName, params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(folder, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }

        return path;
    }

    private static (string, byte[]) Text(string name, string content) => (name, Encoding.UTF8.GetBytes(content));

    private static (string, byte[]) ContainerXml(string opfPath = "OEBPS/content.opf") => Text("META-INF/container.xml", $"""
        <?xml version="1.0"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles><rootfile full-path="{opfPath}" media-type="application/oebps-package+xml"/></rootfiles>
        </container>
        """);

    private static (string, byte[]) Opf(string metadata, string manifest, string path = "OEBPS/content.opf") => Text(path, $"""
        <?xml version="1.0"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">{metadata}</metadata>
          <manifest>{manifest}</manifest>
        </package>
        """);

    /// <summary>A small EPUB 3 with its cover declared by <c>properties="cover-image"</c>.</summary>
    internal static string WriteEpubWithCover(string folder, string fileName) => WriteZip(folder, fileName,
        ContainerXml(),
        Opf("", """<item id="c" href="images/front.jpg" media-type="image/jpeg" properties="cover-image"/>"""),
        ("OEBPS/images/front.jpg", Jpeg));

    [Fact]
    public void Extract_Epub3CoverImageProperty()
    {
        var cover = BookCoverExtractor.Extract(WriteEpubWithCover(_root, "book.epub"));

        Assert.Equal(Jpeg, cover!.Bytes);
        Assert.Equal("image/jpeg", cover.ContentType);
    }

    [Fact]
    public void Extract_Epub2CoverMeta_WithAnEncodedRelativeAddress()
    {
        var path = WriteZip(_root, "book.epub",
            ContainerXml("OEBPS/Text/content.opf"),
            Opf("""<meta name="cover" content="my-cover"/>""",
                """<item id="my-cover" href="../Images/Front%20Cover.png" media-type="image/png"/>""", "OEBPS/Text/content.opf"),
            ("OEBPS/Images/Front Cover.png", Png));

        Assert.Equal("image/png", BookCoverExtractor.Extract(path)!.ContentType);
    }

    [Fact]
    public void Extract_NoDeclaredCover_FallsBackToAnImageNamedCover()
    {
        var path = WriteZip(_root, "book.epub",
            ContainerXml(),
            Opf("", """<item id="pic1" href="map.jpg" media-type="image/jpeg"/><item id="pic2" href="cover.jpg" media-type="image/jpeg"/>"""),
            ("OEBPS/map.jpg", Png),
            ("OEBPS/cover.jpg", Jpeg));

        Assert.Equal(Jpeg, BookCoverExtractor.Extract(path)!.Bytes);
    }

    [Fact]
    public void Extract_EpubWithoutAnyCover_IsNull()
    {
        var path = WriteZip(_root, "book.epub", ContainerXml(), Opf("", """<item id="t" href="text.xhtml" media-type="application/xhtml+xml"/>"""));

        Assert.Null(BookCoverExtractor.Extract(path));
    }

    [Fact]
    public void Extract_CbzFirstImageByName()
    {
        var path = WriteZip(_root, "comic.cbz", ("ComicInfo.xml", "<x/>"u8.ToArray()), ("page002.png", Png), ("page001.jpg", Jpeg));

        Assert.Equal(Jpeg, BookCoverExtractor.Extract(path)!.Bytes);
    }

    [Theory]
    [InlineData("broken.epub")]
    [InlineData("broken.cbz")]
    public void Extract_NotAZip_IsNull(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "this is not a zip file");

        Assert.Null(BookCoverExtractor.Extract(path));
    }

    [Fact]
    public void Extract_DeclaredCoverIsNotAnImage_IsNull()
    {
        var path = WriteZip(_root, "book.epub",
            ContainerXml(),
            Opf("", """<item id="c" href="cover.jpg" media-type="image/jpeg" properties="cover-image"/>"""),
            Text("OEBPS/cover.jpg", "not really a jpeg"));

        Assert.Null(BookCoverExtractor.Extract(path));
    }

    [Theory]
    [InlineData("book.pdf")]
    [InlineData("book.mobi")]
    public void Extract_FormatWithoutReadableCover_IsNull(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "x");

        Assert.Null(BookCoverExtractor.Extract(path));
    }

    [Fact]
    public async Task CoverCache_ExtractsOnce_AndAFileChangeGetsAFreshCover()
    {
        var bookFolder = Directory.CreateDirectory(Path.Combine(_root, "Author", "Book")).FullName;
        var cacheFolder = Path.Combine(_root, "covers");
        WriteEpubWithCover(bookFolder, "Book.epub");
        var book = OpdsBooks.From(
            "abc", OpdsTestMetadata.Ebook("Book"), bookFolder, DateTimeOffset.UtcNow, 0, "epub", [("Book.epub", 10)])!;
        var cache = new OpdsCoverCache(cacheFolder);

        Assert.Equal(Jpeg, (await cache.GetAsync(book))!.Bytes);
        var cached = Assert.Single(Directory.GetFiles(cacheFolder));
        Assert.Equal(Jpeg, (await cache.GetAsync(book))!.Bytes);
        Assert.Equal(cached, Assert.Single(Directory.GetFiles(cacheFolder)));

        // The file is placed again with another cover: the old cached one is replaced.
        File.Delete(Path.Combine(bookFolder, "Book.epub"));
        WriteZip(bookFolder, "Book.epub", ContainerXml(),
            Opf("", """<item id="c" href="c.png" media-type="image/png" properties="cover-image"/>"""), ("OEBPS/c.png", Png));
        File.SetLastWriteTimeUtc(Path.Combine(bookFolder, "Book.epub"), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("image/png", (await cache.GetAsync(book))!.ContentType);
        Assert.NotEqual(cached, Assert.Single(Directory.GetFiles(cacheFolder)));
    }

    [Fact]
    public async Task CoverCache_BookWithoutCover_IsRememberedAsSuch()
    {
        var bookFolder = Directory.CreateDirectory(Path.Combine(_root, "NoCover")).FullName;
        var cacheFolder = Path.Combine(_root, "covers");
        WriteZip(bookFolder, "Book.epub", ContainerXml(), Opf("", ""));
        var book = OpdsBooks.From("nocover", OpdsTestMetadata.Ebook("Book"), bookFolder, DateTimeOffset.UtcNow, 0, "epub", [("Book.epub", 10)])!;
        var cache = new OpdsCoverCache(cacheFolder);

        Assert.Null(await cache.GetAsync(book));
        Assert.EndsWith(".none", Assert.Single(Directory.GetFiles(cacheFolder)));
        Assert.Null(await cache.GetAsync(book));
    }
}

internal static class OpdsTestMetadata
{
    public static Core.Metadata.TorrentMetadata Ebook(string title) =>
        OrganizerTestSupport.BuildMetadata(title: title) with { MediaType = Core.Metadata.MediaType.Ebook };
}
