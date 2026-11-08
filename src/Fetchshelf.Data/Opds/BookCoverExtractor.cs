using System.IO.Compression;
using System.Xml.Linq;

namespace Fetchshelf.Data.Opds;

/// <summary>A cover image and its MIME type.</summary>
public sealed record BookCover(byte[] Bytes, string ContentType);

/// <summary>Reads the cover image out of a book file: an EPUB's declared cover, or a CBZ's first image.</summary>
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
        if (extension is not (".epub" or ".cbz"))
        {
            return null;
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

    private static ZipArchiveEntry? FirstImage(ZipArchive zip) =>
        zip.Entries
            .Where(entry => ImageExtensions.Contains(Path.GetExtension(entry.FullName).ToLowerInvariant()))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static BookCover? Read(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxCoverBytes)
        {
            return null;
        }

        // The declared length can't be trusted in a broken or crafted zip, so the copy stops at the limit as well.
        using var stream = entry.Open();
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
