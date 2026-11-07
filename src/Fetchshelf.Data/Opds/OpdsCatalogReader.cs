using System.Text.Json;
using Fetchshelf.Core.Opds;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// Builds the OPDS catalog from the database: every linked torrent with an ebook, in a served library, whose media
/// type is an ebook type. Torrents are matched to libraries by their folder, as the Link job does, since a library's
/// name is optional.
/// </summary>
public sealed class OpdsCatalogReader(FetchshelfDbContext dbContext, IReadOnlyList<Library> libraries, OpdsSettings opds)
{
    public async Task<OpdsCatalog> ReadAsync(CancellationToken cancellationToken = default)
    {
        var served = libraries
            .Select((library, index) => (Library: library, Index: index))
            .Where(l => opds.Libraries.Count == 0 || (l.Library.Name is { } name && opds.Libraries.Contains(name)))
            .ToList();
        var servedIndexes = served.Select(l => l.Index).ToHashSet();

        var rows = await dbContext.Torrents.AsNoTracking()
            .Where(t => t.LibraryPath != null && t.ChosenEbookExtension != null)
            .Select(t => new { t.Id, t.MetadataJson, t.LibraryPath, t.LinkedFilesJson, t.ChosenEbookExtension, t.CreatedAt })
            .ToListAsync(cancellationToken);

        var books = new List<OpdsBook>();
        foreach (var row in rows)
        {
            if (LibraryFolders.IndexContaining(libraries, row.LibraryPath!) is not { } index || !servedIndexes.Contains(index))
            {
                continue;
            }

            var files = row.LinkedFilesJson is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(row.LinkedFilesJson) ?? [];
            var book = OpdsBooks.From(
                row.Id, TorrentMetadataSerializer.Deserialize(row.MetadataJson), row.LibraryPath!, row.CreatedAt, index,
                row.ChosenEbookExtension!, files.Select(file => (file.RelativePath, file.SizeBytes)));
            if (book is not null)
            {
                books.Add(book);
            }
        }

        return new OpdsCatalog(books, served.Select(l => new OpdsLibrary(l.Index, NameOf(l.Library))).ToList());
    }

    /// <summary>A library's name, or for an unnamed one the last part of its folder.</summary>
    private static string NameOf(Library library) =>
        !string.IsNullOrWhiteSpace(library.Name) ? library.Name
        : Path.GetFileName(library.LibraryDir.TrimEnd('/', '\\')) is { Length: > 0 } folder ? folder
        : library.LibraryDir;
}
