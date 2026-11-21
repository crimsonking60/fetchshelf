using Fetchshelf.Core.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// Covers for the OPDS catalog and the Books page, kept in a folder next to the database. A book's cover is the image
/// in its own folder (<see cref="FolderCover"/>) when there is one, else the one embedded in its file, read out once. A
/// cached cover is named after the book and its source's size and modification time, so a file placed again, or a
/// folder image added or removed, gets a fresh one. A book without a cover is remembered too, so it isn't opened on
/// every request.
/// </summary>
public sealed class OpdsCoverCache(string folder, ILogger<OpdsCoverCache>? logger = null)
{
    private const string CoverSuffix = ".cover";
    private const string NoCoverSuffix = ".none";

    private readonly ILogger<OpdsCoverCache> _logger = logger ?? NullLogger<OpdsCoverCache>.Instance;

    /// <summary>The book's cover, or null when it has none, its file isn't there, or it can't be read.</summary>
    public async Task<BookCover?> GetAsync(CatalogBook book, CancellationToken cancellationToken = default)
    {
        if (!book.MayHaveCover)
        {
            return null;
        }

        var folderImage = book.HasOwnFolder ? FolderCover.Find(book.Folder) : null;
        var source = folderImage
            ?? (book.EmbeddedCoverFile is { } file ? new FileInfo(Path.Combine(book.Folder, file.RelativePath)) : null);
        if (source is null || !source.Exists)
        {
            return null;
        }

        var key = $"{SafeId(book.Id)}-{(folderImage is null ? "file" : "folder")}-{source.Length}-{source.LastWriteTimeUtc.Ticks}";
        var coverPath = Path.Combine(folder, key + CoverSuffix);
        if (File.Exists(coverPath))
        {
            var bytes = await File.ReadAllBytesAsync(coverPath, cancellationToken);
            return BookCoverExtractor.SniffImageType(bytes) is { } contentType ? new BookCover(bytes, contentType) : null;
        }

        var noCoverPath = Path.Combine(folder, key + NoCoverSuffix);
        if (File.Exists(noCoverPath))
        {
            return null;
        }

        BookCover? cover;
        try
        {
            cover = folderImage is not null ? FolderCover.Read(folderImage.FullName) : BookCoverExtractor.Extract(source.FullName);
        }
        catch (IOException ex)
        {
            _logger.LogWarning("Could not read the cover of {File}: {Error}", source.FullName, ex.Message);
            return null;
        }

        Directory.CreateDirectory(folder);
        RemoveOlderVersions(book.Id);
        if (cover is null)
        {
            _logger.LogDebug("{File} has no cover Fetchshelf can read", source.FullName);
            await File.WriteAllBytesAsync(noCoverPath, [], cancellationToken);
            return null;
        }

        // Written under a temporary name first, so a request reading at the same time never gets half a file.
        var temporaryPath = coverPath + "." + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temporaryPath, cover.Bytes, cancellationToken);
        File.Move(temporaryPath, coverPath, overwrite: true);
        return cover;
    }

    private void RemoveOlderVersions(string id)
    {
        foreach (var old in Directory.EnumerateFiles(folder, SafeId(id) + "-*"))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
                // Another request may be replacing it at the same moment; it is cleaned up next time.
            }
        }
    }

    /// <summary>The id with anything but letters and digits left out, so it is always a safe file name.</summary>
    private static string SafeId(string id) => new(id.Where(char.IsAsciiLetterOrDigit).ToArray());
}
