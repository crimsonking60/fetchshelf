using System.IO.Compression;
using System.Xml.Linq;
using SharpCompress.Archives.Rar;

namespace Fetchshelf.Data.Opds;

/// <summary>A cover image and its MIME type.</summary>
public sealed record BookCover(byte[] Bytes, string ContentType);

/// <summary>
/// Reads the cover image out of a book file: an EPUB's declared cover, a CBZ or CBR's first image, a MOBI, AZW or AZW3's
/// declared cover (<see cref="MobiCoverReader"/>), the cover embedded in an m4b, m4a or mp3 (<see cref="AudioCoverReader"/>),
/// or the picture filling a PDF's first page.
/// </summary>
public static class BookCoverExtractor
{
    /// <summary>Covers larger than this are left out, as MAM covers are.</summary>
    public const int MaxCoverBytes = 5 * 1024 * 1024;

    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    /// <summary>
    /// The cover of the book at <paramref name="path"/>, or null when its format has no cover Fetchshelf can read,
    /// it declares none, or the file isn't a readable zip. Throws only for I/O errors reading the file.
    /// </summary>
    public static BookCover? Extract(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".m4b" or ".m4a" or ".mp3")
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            var bytes = extension == ".mp3" ? AudioCoverReader.ReadId3(stream) : AudioCoverReader.ReadMp4(stream);
            return bytes is not null && SniffImageType(bytes) is { } contentType ? new BookCover(bytes, contentType) : null;
        }

        if (extension == ".pdf")
        {
            return PdfCover(path);
        }

        if (extension is ".mobi" or ".azw" or ".azw3")
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            var bytes = MobiCoverReader.Read(stream);
            return bytes is not null && SniffImageType(bytes) is { } contentType ? new BookCover(bytes, contentType) : null;
        }

        if (extension is not (".epub" or ".cbz" or ".cbr"))
        {
            return null;
        }

        // A comic's extension doesn't always tell the truth: a .cbr may be a zip, and a .cbz a RAR archive.
        if (extension is ".cbz" or ".cbr" && IsRar(path))
        {
            return RarFirstImage(path);
        }

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = extension == ".epub" ? EpubCoverEntry(zip) : FirstImage(zip);
            return entry is null ? null : Read(entry);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// The picture filling a PDF's first page: its largest image when that covers at least half the page, as a scanned or
    /// retail cover is one picture, while a title page of text with a small logo has none that large. Some scans cut the
    /// page into strips of the same width, one above the other (<see cref="PdfStripCover"/>); those are put together. The
    /// page is not drawn, so text printed over the picture isn't in the cover, and a cover drawn as vector graphics isn't
    /// found. A JPEG is taken as it is, a JPEG 2000 picture is turned into a JPEG, and other images are taken only when
    /// PdfPig can turn them into a PNG. Null for a PDF that can't be read, such as an encrypted one.
    /// </summary>
    private static BookCover? PdfCover(string path)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            if (document.NumberOfPages == 0)
            {
                return null;
            }

            var page = document.GetPage(1);
            var pageArea = page.Width * page.Height;
            var images = page.GetImages().ToList();
            var image = images.MaxBy(i => i.BoundingBox.Area);
            if (image is null || pageArea <= 0)
            {
                return null;
            }

            if (image.BoundingBox.Area < pageArea / 2)
            {
                return PdfStripCover.Join(images, pageArea);
            }

            var bytes = PdfImageBytes(image);
            return bytes is { Length: > 0 and <= MaxCoverBytes } && SniffImageType(bytes) is { } contentType ? new BookCover(bytes, contentType) : null;
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException or OperationCanceledException))
        {
            // PdfPig, CoreJ2K and SkiaSharp throw their own exceptions, and others, for damaged, encrypted or unusual files.
            return null;
        }
    }

    /// <summary>
    /// A PDF image as a JPEG when it is one, or is a JPEG 2000 picture (<see cref="Jpeg2000Picture"/>), else as a PNG when
    /// PdfPig can make one; null otherwise, such as for the black-and-white JBIG2 and CCITT fax pictures of some scans.
    /// </summary>
    internal static byte[]? PdfImageBytes(UglyToad.PdfPig.Content.IPdfImage image)
    {
        var bytes = image.RawMemory.ToArray();
        return SniffImageType(bytes) is "image/jpeg" ? bytes
            : Jpeg2000Picture.Is(bytes) ? Jpeg2000Picture.ToJpeg(bytes)
            : image.TryGetPng(out var png) ? png : null;
    }

    /// <summary>The image type of <paramref name="bytes"/> from its first bytes, or null when it isn't a known image.</summary>
    public static string? SniffImageType(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]) ? "image/png"
        : bytes.StartsWith("GIF8"u8) ? "image/gif"
        : bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    /// <summary>
    /// The cover declared in the EPUB's package: the manifest item with <c>properties="cover-image"</c> (EPUB 3), else
    /// the item named by <c>&lt;meta name="cover"&gt;</c> (EPUB 2), else an image item whose id or address says "cover".
    /// </summary>
    private static ZipArchiveEntry? EpubCoverEntry(ZipArchive zip)
    {
        var containerEntry = zip.GetEntry("META-INF/container.xml");
        if (containerEntry is null)
        {
            return null;
        }

        string? opfPath;
        using (var stream = containerEntry.Open())
        {
            opfPath = (string?)XDocument.Load(stream).Descendants(Container + "rootfile").FirstOrDefault()?.Attribute("full-path");
        }

        if (string.IsNullOrEmpty(opfPath) || zip.GetEntry(opfPath) is not { } opfEntry)
        {
            return null;
        }

        XDocument opf;
        using (var stream = opfEntry.Open())
        {
            opf = XDocument.Load(stream);
        }

        var items = opf.Descendants(Opf + "item").ToList();
        var coverId = (string?)opf.Descendants(Opf + "meta").FirstOrDefault(meta => (string?)meta.Attribute("name") == "cover")?.Attribute("content");
        var item =
            items.FirstOrDefault(i => ((string?)i.Attribute("properties"))?.Split(' ').Contains("cover-image") == true)
            ?? items.FirstOrDefault(i => coverId is not null && (string?)i.Attribute("id") == coverId)
            ?? items.FirstOrDefault(i => ((string?)i.Attribute("media-type"))?.StartsWith("image/", StringComparison.Ordinal) == true
                && (((string?)i.Attribute("id"))?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true
                    || ((string?)i.Attribute("href"))?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true));
        if ((string?)item?.Attribute("href") is not { Length: > 0 } href)
        {
            return null;
        }

        // The address is relative to the package file, and may be percent-encoded.
        var folder = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        var entryPath = NormalizePath(folder.Length == 0 ? Uri.UnescapeDataString(href) : $"{folder}/{Uri.UnescapeDataString(href)}");
        return zip.GetEntry(entryPath);
    }

    private static bool IsRar(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4 && magic.SequenceEqual("Rar!"u8);
    }

    /// <summary>The first image of a RAR archive, as for a zip; null when it has none or isn't a readable RAR archive.</summary>
    private static BookCover? RarFirstImage(string path)
    {
        try
        {
            using var archive = RarArchive.OpenArchive(path);
            var entry = archive.Entries
                .Where(e => !e.IsDirectory && e.Key is { } key && ImageExtensions.Contains(Path.GetExtension(key).ToLowerInvariant()))
                .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(e => e.Size <= MaxCoverBytes);
            if (entry is null)
            {
                return null;
            }

            using var stream = entry.OpenEntryStream();
            return ReadCapped(stream);
        }
        catch (Exception ex) when (ex is SharpCompress.Common.ArchiveException or SharpCompress.Common.InvalidFormatException or InvalidOperationException
            or NotSupportedException or IndexOutOfRangeException or ArgumentException)
        {
            // A broken, encrypted or unusual archive: no cover rather than an error.
            return null;
        }
    }

    /// <summary>
    /// The first image by name small enough to be a cover: a comic sometimes starts with a large wraparound scan, and
    /// the front cover follows it.
    /// </summary>
    private static ZipArchiveEntry? FirstImage(ZipArchive zip) =>
        zip.Entries
            .Where(entry => ImageExtensions.Contains(Path.GetExtension(entry.FullName).ToLowerInvariant()))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(entry => entry.Length <= MaxCoverBytes);

    private static BookCover? Read(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxCoverBytes)
        {
            return null;
        }

        // The declared length can't be trusted in a broken or crafted zip, so the copy stops at the limit as well.
        using var stream = entry.Open();
        return ReadCapped(stream);
    }

    /// <summary>An image read from <paramref name="stream"/>, or null when it is larger than the cap or not an image.</summary>
    private static BookCover? ReadCapped(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxCoverBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        return SniffImageType(bytes) is { } contentType ? new BookCover(bytes, contentType) : null;
    }

    /// <summary>Resolves "." and ".." in a path inside the zip.</summary>
    private static string NormalizePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part is not ("." or ""))
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }
}
