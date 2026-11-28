namespace Fetchshelf.Core.Catalog;

/// <summary>Which of a book's files: its audio or its ebook files.</summary>
public enum CatalogFileKind
{
    Audio,
    Ebook,
}

/// <summary>How a book's files are served: their content types and their download names.</summary>
public static class CatalogFiles
{
    public static IReadOnlyList<CatalogFile> Of(CatalogBook book, CatalogFileKind kind) =>
        kind == CatalogFileKind.Audio ? book.AudioFiles : book.EbookFiles;

    /// <summary>The content type a file is served with, by its extension.</summary>
    public static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        "epub" => "application/epub+zip",
        "pdf" => "application/pdf",
        "mobi" => "application/x-mobipocket-ebook",
        // AZW3 (KF8) has no registered type; calibre serves it as this one. Safari takes application/vnd.amazon.ebook for
        // a .mobi and adds that to the name of an .azw3 it downloads.
        "azw3" => "application/x-mobi8-ebook",
        "azw" => "application/vnd.amazon.ebook",
        "cbz" => "application/vnd.comicbook+zip",
        "cbr" => "application/vnd.comicbook-rar",
        "m4b" or "m4a" => "audio/mp4",
        "mp3" => "audio/mpeg",
        "ogg" => "audio/ogg",
        "flac" => "audio/flac",
        _ => "application/octet-stream",
    };

    /// <summary>The name a downloaded file gets: "Title - Author.epub", with a part number when there are several files.</summary>
    public static string DownloadName(CatalogBook book, IReadOnlyList<CatalogFile> files, int index) =>
        NamedAfter(book, files.Count > 1 ? $" ({index + 1})" : string.Empty, files[index].Extension);

    /// <summary>"Title - Author" with <paramref name="suffix"/> and the extension, left with only characters a file name may have.</summary>
    public static string NamedAfter(CatalogBook book, string suffix, string extension)
    {
        var name = (book.Authors.Count > 0 ? $"{book.Title} - {book.Authors[0]}" : book.Title) + suffix;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        return $"{(safe.Length > 0 ? safe : "book")}.{extension}";
    }
}
