using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Opds;

/// <summary>What the OPDS catalog serves of the <see cref="BookCatalog"/>: the books to read, in the served libraries.</summary>
public static class OpdsCatalog
{
    /// <summary>
    /// The media types whose books are served. An audiobook's companion PDF, linked as its ebook side, is left out:
    /// the catalog is for books to read.
    /// </summary>
    public static readonly IReadOnlySet<MediaType> EbookMediaTypes =
        new HashSet<MediaType> { MediaType.Ebook, MediaType.Manga, MediaType.ComicBook, MediaType.PeriodicalEbook };

    /// <summary>Whether a book is served: an ebook media type with an ebook file.</summary>
    public static bool Serves(CatalogBook book) => EbookMediaTypes.Contains(book.MediaType) && book.EbookFiles.Count > 0;

    /// <param name="catalog">Every linked book.</param>
    /// <param name="servedLibraries">The indexes of the served libraries; null serves them all.</param>
    public static BookCatalog From(BookCatalog catalog, IReadOnlySet<int>? servedLibraries) => catalog.Narrow(Serves, servedLibraries);
}
