using System.IO.Compression;
using System.Text;
using Fetchshelf.Core.Catalog;
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

    [Fact]
    public void Extract_CbzSkipsAnImageTooLargeToBeACover()
    {
        var path = WriteZip(_root, "comic.cbz", ("page000 wrap.jpg", [.. Jpeg, .. new byte[BookCoverExtractor.MaxCoverBytes]]), ("page001.png", Png));

        Assert.Equal(Png, BookCoverExtractor.Extract(path)!.Bytes);
    }

    /// <summary>
    /// A RAR 4 archive with its files stored uncompressed, built by hand since nothing here writes RAR: a marker, the
    /// archive header, a header and the data for each file, and the end block. Each header's check is the low half of
    /// the CRC-32 of the rest of it.
    /// </summary>
    internal static string WriteRar(string folder, string fileName, params (string Name, byte[] Bytes)[] entries)
    {
        static byte[] Block(byte type, ushort flags, byte[] body)
        {
            byte[] rest = [type, .. BitConverter.GetBytes(flags), .. BitConverter.GetBytes((ushort)(7 + body.Length)), .. body];
            return [.. BitConverter.GetBytes((ushort)(Crc32(rest) & 0xFFFF)), .. rest];
        }

        var file = new List<byte>("Rar!\x1a\x07\x00"u8.ToArray());
        file.AddRange(Block(0x73, 0, new byte[6]));
        foreach (var (name, bytes) in entries)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            byte[] header =
            [
                .. BitConverter.GetBytes((uint)bytes.Length), .. BitConverter.GetBytes((uint)bytes.Length), 0,
                .. BitConverter.GetBytes(Crc32(bytes)), .. BitConverter.GetBytes(0x5A210000u),
                29, 0x30, .. BitConverter.GetBytes((ushort)nameBytes.Length), .. BitConverter.GetBytes(0x20u), .. nameBytes,
            ];
            file.AddRange(Block(0x74, 0x8000, header));
            file.AddRange(bytes);
        }

        file.AddRange(Block(0x7B, 0x4000, []));
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, [.. file]);
        return path;
    }

    /// <summary>The CRC-32 RAR and zip use (polynomial 0xEDB88320), bit by bit: short inputs only.</summary>
    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    [Fact]
    public void Extract_CbrFirstImageByName()
    {
        var path = WriteRar(_root, "comic.cbr", ("ComicInfo.xml", "<x/>"u8.ToArray()), ("page002.png", Png), ("page001.jpg", Jpeg));

        Assert.Equal(Jpeg, BookCoverExtractor.Extract(path)!.Bytes);
    }

    [Fact]
    public void Extract_ComicsAreReadByWhatTheyAre_NotTheirExtension()
    {
        var zipNamedCbr = WriteZip(_root, "zip.cbr", ("page001.jpg", Jpeg));
        var rarNamedCbz = WriteRar(_root, "rar.cbz", ("page001.png", Png));

        Assert.Equal(Jpeg, BookCoverExtractor.Extract(zipNamedCbr)!.Bytes);
        Assert.Equal(Png, BookCoverExtractor.Extract(rarNamedCbz)!.Bytes);
    }

    [Theory]
    [InlineData("broken.epub")]
    [InlineData("broken.cbz")]
    [InlineData("broken.cbr")]
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
        var book = CatalogBooks.From(
            "abc", OpdsTestMetadata.Ebook("Book"), bookFolder, DateTimeOffset.UtcNow, 0, null, "epub", [("Book.epub", 10)])!;
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
        var book = CatalogBooks.From("nocover", OpdsTestMetadata.Ebook("Book"), bookFolder, DateTimeOffset.UtcNow, 0, null, "epub", [("Book.epub", 10)])!;
        var cache = new OpdsCoverCache(cacheFolder);

        Assert.Null(await cache.GetAsync(book));
        Assert.EndsWith(".none", Assert.Single(Directory.GetFiles(cacheFolder)));
        Assert.Null(await cache.GetAsync(book));
    }

    /// <summary>An MP4 box: its size, its four-letter type and its contents.</summary>
    private static byte[] Box(string type, params byte[][] contents)
    {
        var body = contents.SelectMany(part => part).ToArray();
        var size = BitConverter.GetBytes(8 + body.Length);
        Array.Reverse(size);
        return [.. size, .. Encoding.ASCII.GetBytes(type), .. body];
    }

    /// <summary>An m4b holding <paramref name="cover"/> in moov/udta/meta/ilst/covr/data, its audio first or last.</summary>
    private string WriteM4b(byte[]? cover, bool moovLast = false)
    {
        var ilst = cover is null ? Box("ilst") : Box("ilst", Box("covr", Box("data", [0, 0, 0, 13, 0, 0, 0, 0], cover)));
        var moov = Box("moov", Box("mvhd", new byte[20]), Box("udta", Box("meta", [0, 0, 0, 0], Box("hdlr", new byte[25]), ilst)));
        var mdat = Box("mdat", new byte[5000]);
        byte[] file = [.. Box("ftyp", "M4B "u8.ToArray()), .. moovLast ? mdat : moov, .. moovLast ? moov : mdat];
        var path = Path.Combine(_root, $"book-{Guid.NewGuid():N}.m4b");
        File.WriteAllBytes(path, file);
        return path;
    }

    private static byte[] SyncSafe(int value) =>
        [(byte)((value >> 21) & 0x7F), (byte)((value >> 14) & 0x7F), (byte)((value >> 7) & 0x7F), (byte)(value & 0x7F)];

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>An ID3v2.3 or 2.4 frame.</summary>
    private static byte[] Frame(int version, string id, byte[] data) =>
        [.. Encoding.ASCII.GetBytes(id), .. version == 4 ? SyncSafe(data.Length) : BigEndian(data.Length), 0, 0, .. data];

    /// <summary>An APIC frame's contents: Latin-1, the MIME type, the picture type and a description.</summary>
    private static byte[] Apic(byte pictureType, byte[] image, byte encoding = 0) =>
        [encoding, .. "image/jpeg"u8.ToArray(), 0, pictureType, .. encoding == 1 ? new byte[] { 0xFF, 0xFE, (byte)'x', 0, 0, 0 } : [(byte)'x', 0], .. image];

    private string WriteMp3(int version, params byte[][] frames)
    {
        var body = frames.SelectMany(frame => frame).ToArray();
        byte[] file = [.. "ID3"u8.ToArray(), (byte)version, 0, 0, .. SyncSafe(body.Length + 10), .. body, .. new byte[10], 0xFF, 0xFB, .. new byte[400]];
        var path = Path.Combine(_root, $"book-{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, file);
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void M4b_CoverIsReadWhereverTheMoovBoxIs(bool moovLast) =>
        Assert.Equal(Png, BookCoverExtractor.Extract(WriteM4b(Png, moovLast))!.Bytes);

    [Fact]
    public void M4b_WithoutCover_OrNotAnMp4_HasNone()
    {
        var notMp4 = Path.Combine(_root, "noise.m4b");
        File.WriteAllBytes(notMp4, Encoding.ASCII.GetBytes("this is not an mp4 file at all"));

        Assert.Null(BookCoverExtractor.Extract(WriteM4b(cover: null)));
        Assert.Null(BookCoverExtractor.Extract(notMp4));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Mp3_FrontCoverIsPreferred_OverAnEarlierPicture(int version)
    {
        var path = WriteMp3(version,
            Frame(version, "TIT2", [0, .. "Dune"u8.ToArray()]),
            Frame(version, "APIC", Apic(0, Png)),
            Frame(version, "APIC", Apic(3, Jpeg, encoding: 1)));

        Assert.Equal(Jpeg, BookCoverExtractor.Extract(path)!.Bytes);
    }

    [Fact]
    public void Mp3_OnlyAnotherPicture_IsUsed_AndNoTagMeansNoCover()
    {
        var other = WriteMp3(3, Frame(3, "APIC", Apic(0, Png)));
        var noTag = Path.Combine(_root, "plain.mp3");
        File.WriteAllBytes(noTag, [0xFF, 0xFB, .. new byte[100]]);

        Assert.Equal(Png, BookCoverExtractor.Extract(other)!.Bytes);
        Assert.Null(BookCoverExtractor.Extract(noTag));
        Assert.Null(BookCoverExtractor.Extract(WriteMp3(3, Frame(3, "TIT2", [0, .. "Dune"u8.ToArray()]))));
    }

    [Fact]
    public void Mp3_Id3v22Picture_IsRead()
    {
        byte[] pic = [0, .. "JPG"u8.ToArray(), 3, 0, .. Jpeg];
        byte[] frame = [.. "PIC"u8.ToArray(), 0, 0, (byte)pic.Length, .. pic];
        Assert.Equal(Jpeg, BookCoverExtractor.Extract(WriteMp3(2, frame))!.Bytes);
    }

    [Theory]
    [InlineData(new[] { "back.jpg", "Cover.png", "folder.jpg" }, "Cover.png")]
    [InlineData(new[] { "back.jpg", "folder.jpg" }, "folder.jpg")]
    [InlineData(new[] { "scan.jpg" }, "scan.jpg")]
    [InlineData(new[] { "one.jpg", "two.jpg" }, null)]
    [InlineData(new string[0], null)]
    public void FolderCover_PrefersCover_ThenFolder_ThenTheOnlyImage(string[] images, string? expected)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Folder")).FullName;
        File.WriteAllText(Path.Combine(folder, "Book.m4b"), "audio");
        foreach (var image in images)
        {
            File.WriteAllBytes(Path.Combine(folder, image), Jpeg);
        }

        Assert.Equal(expected, FolderCover.Find(folder)?.Name);
    }

    [Fact]
    public async Task CoverCache_FolderImageComesFirst_AndWhenItGoesTheBookFilesCoverIsUsed()
    {
        var bookFolder = Directory.CreateDirectory(Path.Combine(_root, "Author", "Audio")).FullName;
        File.Copy(WriteM4b(Png), Path.Combine(bookFolder, "Book.m4b"));
        File.WriteAllBytes(Path.Combine(bookFolder, "cover.jpg"), Jpeg);
        var book = CatalogBooks.From(
            "audio", OpdsTestMetadata.Ebook("Book"), bookFolder, DateTimeOffset.UtcNow, 0, "m4b", null, [("Book.m4b", 10)])!;
        var cache = new OpdsCoverCache(Path.Combine(_root, "covers"));

        Assert.Equal(Jpeg, (await cache.GetAsync(book))!.Bytes);
        File.Delete(Path.Combine(bookFolder, "cover.jpg"));
        Assert.Equal(Png, (await cache.GetAsync(book))!.Bytes);
    }

    [Fact]
    public async Task CoverCache_PdfGetsItsFolderImage_ButNotInASharedSeriesFolder()
    {
        var bookFolder = Directory.CreateDirectory(Path.Combine(_root, "Author", "Pdf")).FullName;
        File.WriteAllText(Path.Combine(bookFolder, "Book.pdf"), "%PDF");
        File.WriteAllBytes(Path.Combine(bookFolder, "cover.jpg"), Jpeg);
        var book = CatalogBooks.From(
            "pdf", OpdsTestMetadata.Ebook("Book"), bookFolder, DateTimeOffset.UtcNow, 0, null, "pdf", [("Book.pdf", 4)])!;
        var cache = new OpdsCoverCache(Path.Combine(_root, "covers"));

        Assert.Equal(Jpeg, (await cache.GetAsync(book))!.Bytes);
        Assert.Null(await cache.GetAsync(book with { HasOwnFolder = false }));
    }

    [Fact]
    public async Task CoverCache_Prune_RemovesBooksNoLongerLinked_AndAbandonedWrites_AtMostOnceAWeek()
    {
        var cacheFolder = Path.Combine(_root, "covers");
        var cache = new OpdsCoverCache(cacheFolder);
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        Assert.Null(cache.PruneIfDue(["kept"], now));

        foreach (var id in new[] { "kept", "gone-3", "nocover" })
        {
            var bookFolder = Directory.CreateDirectory(Path.Combine(_root, "books", id)).FullName;
            if (id == "nocover")
            {
                WriteZip(bookFolder, "Book.epub", ContainerXml(), Opf("", ""));
            }
            else
            {
                WriteEpubWithCover(bookFolder, "Book.epub");
            }

            await cache.GetAsync(CatalogBooks.From(id, OpdsTestMetadata.Ebook("Book"), bookFolder, now, 0, null, "epub", [("Book.epub", 10)])!);
        }

        var kept = Directory.GetFiles(cacheFolder).Single(file => Path.GetFileName(file).StartsWith("kept-", StringComparison.Ordinal));
        var abandoned = kept + ".0123abc";
        var writing = kept + ".4567def";
        File.WriteAllBytes(abandoned, Jpeg);
        File.SetLastWriteTimeUtc(abandoned, now.UtcDateTime.AddHours(-2));
        File.WriteAllBytes(writing, Jpeg);
        File.SetLastWriteTimeUtc(writing, now.UtcDateTime.AddMinutes(-1));

        // "gone-3" is cached as "gone3": the removed one is found by the same name.
        Assert.Equal(3, cache.PruneIfDue(["kept", "nocover-x"], now));
        Assert.Equal([kept, writing], Directory.GetFiles(cacheFolder).Where(file => !file.EndsWith(".pruned", StringComparison.Ordinal)).Order());

        Assert.Null(cache.PruneIfDue([], now.AddDays(6)));
        Assert.Equal(2, cache.PruneIfDue([], now.AddDays(8)));
    }
}

internal static class OpdsTestMetadata
{
    public static Core.Metadata.TorrentMetadata Ebook(string title) =>
        OrganizerTestSupport.BuildMetadata(title: title) with { MediaType = Core.Metadata.MediaType.Ebook };
}
