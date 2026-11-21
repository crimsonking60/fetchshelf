using System.Text.Json;
using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Catalog;

/// <summary>
/// Builds the <see cref="BookCatalog"/> from the database: every linked torrent with an audio or ebook file in a
/// configured library. Torrents are matched to libraries by their folder, as the Link job does, since a library's name
/// is optional. A split collection's books are each their own record with their own folder; the collection's own record
/// has none and is left out.
/// </summary>
public sealed class BookCatalogReader(FetchshelfDbContext dbContext, IReadOnlyList<Library> libraries)
{
    public async Task<BookCatalog> ReadAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.LibraryPath != null && (t.ChosenAudioExtension != null || t.ChosenEbookExtension != null))
            .Select(t => new { t.Id, t.MetadataJson, t.LibraryPath, t.LinkedFilesJson, t.ChosenAudioExtension, t.ChosenEbookExtension, t.CreatedAt, t.AbsId })
            .ToListAsync(cancellationToken);

        var books = new List<CatalogBook>();
        foreach (var row in rows)
        {
            if (LibraryFolders.IndexContaining(libraries, row.LibraryPath!) is not { } index)
            {
                continue;
            }

            var files = row.LinkedFilesJson is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(row.LinkedFilesJson) ?? [];
            var book = CatalogBooks.From(
                row.Id, TorrentMetadataSerializer.Deserialize(row.MetadataJson), row.LibraryPath!, row.CreatedAt, index,
                row.ChosenAudioExtension, row.ChosenEbookExtension, files.Select(file => (file.RelativePath, file.SizeBytes)));
            if (book is not null)
            {
                books.Add(book with { AbsId = row.AbsId, HasOwnFolder = !libraries[index].IsSeriesLayout });
            }
        }

        return new BookCatalog(books, libraries.Select((library, index) => new CatalogLibrary(index, NameOf(library))).ToList());
    }

    /// <summary>A library's name, or for an unnamed one the last part of its folder.</summary>
    public static string NameOf(Library library) =>
        !string.IsNullOrWhiteSpace(library.Name) ? library.Name
        : Path.GetFileName(library.LibraryDir.TrimEnd('/', '\\')) is { Length: > 0 } folder ? folder
        : library.LibraryDir;
}
