using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Web.Catalog;

/// <summary>Serves one of a book's files, for the OPDS catalog and the Books page alike.</summary>
public static class BookFiles
{
    /// <summary>
    /// The book's file at <paramref name="index"/> among its files of <paramref name="kind"/>, with range support and a
    /// "Title - Author.ext" name. Nothing from the request becomes part of the path: the book and file come from the
    /// catalog, and the stored relative path must still lead inside the book's folder.
    /// </summary>
    public static IResult Serve(CatalogBook book, CatalogFileKind kind, int index, ILogger logger)
    {
        var files = CatalogFiles.Of(book, kind);
        if (index < 0 || index >= files.Count)
        {
            return Results.NotFound();
        }

        var folder = Path.GetFullPath(book.Folder);
        var path = Path.GetFullPath(Path.Combine(folder, files[index].RelativePath));
        if (path == folder.TrimEnd(Path.DirectorySeparatorChar) || !PathPrefix.IsSameOrUnder(path, folder))
        {
            return Results.NotFound();
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("Download of {Title}: {Path} is missing", book.Title, path);
            return Results.NotFound();
        }

        return Results.File(
            path, CatalogFiles.ContentTypeFor(files[index].Extension), CatalogFiles.DownloadName(book, files, index), enableRangeProcessing: true);
    }
}
