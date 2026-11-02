namespace Fetchshelf.Data.Organizer;

/// <summary>Removes what Fetchshelf placed in a book's library folder, and nothing else.</summary>
public static class LibraryFileRemover
{
    private static readonly string[] Extras = ["cover.jpg", "metadata.json"];

    /// <summary>
    /// Removes <paramref name="linkedFiles"/> from <paramref name="libraryPath"/>, then `cover.jpg`, `metadata.json`,
    /// sub-folders (such as `Disc 1`) left empty, and the folder itself when empty. Files already gone are skipped.
    /// Returns the relative paths of the linked files that were removed.
    /// </summary>
    public static List<string> Remove(string libraryPath, IEnumerable<LinkedFileRecord> linkedFiles)
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

        foreach (var extra in Extras)
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

    private static bool IsEmptyFolder(string path) => Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0;
}
