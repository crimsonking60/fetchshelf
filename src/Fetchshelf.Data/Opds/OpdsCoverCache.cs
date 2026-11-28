using Fetchshelf.Core.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fetchshelf.Data.Opds;

/// <summary>
/// Covers for the OPDS catalog and the Books page, kept in a folder next to the database. A book's cover is the image
/// in its own folder (<see cref="FolderCover"/>) when there is one, else the one embedded in its file, read out once. A
/// cached cover is named after the book and its source's size and modification time, so a file placed again, or a
/// folder image added or removed, gets a fresh one. A book without a cover is remembered too, so it isn't opened on
/// every request. Covers of books no longer in the library are removed by <see cref="PruneIfDue"/>.
/// </summary>
public sealed class OpdsCoverCache(string folder, ILogger<OpdsCoverCache>? logger = null)
{
    /// <summary>How often <see cref="PruneIfDue"/> prunes.</summary>
    public static readonly TimeSpan PruneEvery = TimeSpan.FromDays(7);

    /// <summary>
    /// The cache name's part for a cover read out of the book's file, with the version of how it is read: raised when
    /// covers come out differently, so each is read again, and the old ones are pruned (from 2, the stencil of a layered
    /// PDF scan was taken for the cover).
    /// </summary>
    private const string FileKind = "file2";

    private const string FolderKind = "folder";

    private const string CoverSuffix = ".cover";
    private const string NoCoverSuffix = ".none";

    /// <summary>An empty file whose modification time is when the cache was last pruned.</summary>
    private const string PrunedMarker = ".pruned";

    /// <summary>A cover being written is renamed into place at once; one left behind this long was interrupted.</summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);

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

        var key = $"{SafeId(book.Id)}-{(folderImage is null ? FileKind : FolderKind)}-{source.Length}-{source.LastWriteTimeUtc.Ticks}";
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

    /// <summary>
    /// Removes the cached covers, and "no cover" notes, of every book not in <paramref name="linkedIds"/>, and covers
    /// left half written; at most once every <see cref="PruneEvery"/>, as the last prune's time is kept in the folder.
    /// </summary>
    /// <param name="linkedIds">The ids of every book in a library.</param>
    /// <returns>How many files were removed, or null when the last prune was too recent or there is no cache yet.</returns>
    public int? PruneIfDue(IEnumerable<string> linkedIds, DateTimeOffset now)
    {
        var marker = new FileInfo(Path.Combine(folder, PrunedMarker));
        if (!Directory.Exists(folder) || (marker.Exists && now - new DateTimeOffset(marker.LastWriteTimeUtc, TimeSpan.Zero) < PruneEvery))
        {
            return null;
        }

        var kept = linkedIds.Select(SafeId).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
        {
            if (file.Name == PrunedMarker)
            {
                continue;
            }

            var parts = file.Name.Split('-');
            var id = parts[0];
            var abandoned = !file.Name.EndsWith(CoverSuffix, StringComparison.Ordinal) && !file.Name.EndsWith(NoCoverSuffix, StringComparison.Ordinal)
                && now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) > AbandonedAfter;
            // A cover read the way an older version read it is never asked for again.
            var outdated = parts.Length > 1 && parts[1] is not (FileKind or FolderKind);
            if (kept.Contains(id) && !abandoned && !outdated)
            {
                continue;
            }

            try
            {
                file.Delete();
                removed++;
            }
            catch (IOException ex)
            {
                _logger.LogWarning("Could not remove the cached cover {File}: {Error}", file.FullName, ex.Message);
            }
        }

        File.WriteAllBytes(marker.FullName, []);
        File.SetLastWriteTimeUtc(marker.FullName, now.UtcDateTime);
        _logger.LogInformation("Cover cache pruned: {Removed} files of books no longer in the library removed", removed);
        return removed;
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
