namespace Fetchshelf.Core.Catalog;

/// <summary>Which of a book's files: its audio or its ebook files.</summary>
public enum CatalogFileKind
{
    Audio,
    Ebook,
}

/// <summary>What the Books page's format filter keeps.</summary>
public enum CatalogFormat
{
    All,
    Audiobooks,
    Ebooks,
}

/// <summary>How a book's files are served: their content types, their download names, and the format filter.</summary>
public static class CatalogFiles
{
    public static IReadOnlyList<CatalogFile> Of(CatalogBook book, CatalogFileKind kind) =>
        kind == CatalogFileKind.Audio ? book.AudioFiles : book.EbookFiles;

    /// <summary>
    /// Whether a book passes the format filter: an audiobook has audio files; an ebook is a book to read with an ebook
    /// file, so an audiobook's companion PDF doesn't make it one (as in the OPDS catalog).
    /// </summary>
    public static bool Matches(CatalogBook book, CatalogFormat format) => format switch
    {
        CatalogFormat.Audiobooks => book.AudioFiles.Count > 0,
        CatalogFormat.Ebooks => Opds.OpdsCatalog.Serves(book),
        _ => true,
    };

    /// <summary>The content type a file is served with, by its extension.</summary>
    public static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        "epub" => "application/epub+zip",
        "pdf" => "application/pdf",
        "mobi" => "application/x-mobipocket-ebook",
        "azw3" or "azw" => "application/vnd.amazon.ebook",
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
