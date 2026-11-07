using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Organizer;

/// <summary>Removes what Fetchshelf placed in a book's library folder, and nothing else.</summary>
public static class LibraryFileRemover
{
    private static readonly string[] Extras = ["cover.jpg", "metadata.json", "metadata.opf"];

    /// <summary>
    /// Removes <paramref name="linkedFiles"/> from <paramref name="libraryPath"/>, then `cover.jpg`, `metadata.json`,
    /// `metadata.opf`, sub-folders (such as `Disc 1`) left empty, and the folder itself when empty. Files already gone are skipped.
    /// Returns the relative paths of the linked files that were removed.
    /// </summary>
    /// <param name="folderSharedWithAnotherTorrent">
    /// True when another linked torrent still uses the folder, such as a better format of the same book; its
    /// `cover.jpg`, `metadata.json` and `metadata.opf` are then left in place.
    /// </param>
    public static List<string> Remove(string libraryPath, IEnumerable<LinkedFileRecord> linkedFiles, bool folderSharedWithAnotherTorrent)
    {
        var removed = new List<string>();
        foreach (var file in linkedFiles)
        {
            var path = Path.Combine(libraryPath, file.RelativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
                removed.Add(file.RelativePath);
            }

            var subFolder = Path.GetDirectoryName(path);
            if (subFolder is not null && subFolder != libraryPath.TrimEnd('/') && IsEmptyFolder(subFolder))
            {
                Directory.Delete(subFolder);
            }
        }

        foreach (var extra in folderSharedWithAnotherTorrent ? [] : Extras)
        {
            var path = Path.Combine(libraryPath, extra);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (IsEmptyFolder(libraryPath))
        {
            Directory.Delete(libraryPath);
        }

        return removed;
    }

    /// <summary>Whether a linked torrent other than <paramref name="torrentId"/> also uses <paramref name="libraryPath"/>.</summary>
    public static Task<bool> IsFolderSharedAsync(
        FetchshelfDbContext dbContext, string libraryPath, string torrentId, CancellationToken cancellationToken) =>
        dbContext.Torrents.AnyAsync(t => t.LibraryPath == libraryPath && t.Id != torrentId, cancellationToken);

    private static bool IsEmptyFolder(string path) => Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0;
}
