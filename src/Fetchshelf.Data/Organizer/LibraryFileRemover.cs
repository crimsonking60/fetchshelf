using System.Text.Json;
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
    /// <param name="filesUsedByOthers">
    /// Relative paths another linked torrent in the same folder also lists (<see cref="FolderUseByOthersAsync"/>). They
    /// are left in place: a record can list a file it found already there, which belongs to the other torrent.
    /// </param>
    public static List<string> Remove(
        string libraryPath, IEnumerable<LinkedFileRecord> linkedFiles, bool folderSharedWithAnotherTorrent,
        IReadOnlySet<string>? filesUsedByOthers = null)
    {
        var removed = new List<string>();
        foreach (var file in linkedFiles)
        {
            if (filesUsedByOthers?.Contains(file.RelativePath) == true)
            {
                continue;
            }

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

    /// <summary>
    /// How linked torrents other than <paramref name="torrentId"/> use <paramref name="libraryPath"/>: whether any does,
    /// and the relative paths of the files they list there, compared ignoring case.
    /// </summary>
    public static async Task<FolderUse> FolderUseByOthersAsync(
        FetchshelfDbContext dbContext, string libraryPath, string torrentId, CancellationToken cancellationToken)
    {
        var others = await dbContext.Torrents
            .Where(t => t.LibraryPath == libraryPath && t.Id != torrentId)
            .Select(t => t.LinkedFilesJson)
            .ToListAsync(cancellationToken);
        var files = others
            .SelectMany(json => json is null ? [] : JsonSerializer.Deserialize<List<LinkedFileRecord>>(json) ?? [])
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new FolderUse(others.Count > 0, files);
    }

    private static bool IsEmptyFolder(string path) => Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0;
}

/// <summary>What other linked torrents use of a library folder (<see cref="LibraryFileRemover.FolderUseByOthersAsync"/>).</summary>
public sealed record FolderUse(bool Shared, IReadOnlySet<string> Files);
