using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Data.Organizer;

/// <summary>An image taken out of a book's folder to be put into its new one: its file name and bytes.</summary>
public sealed record FolderImage(string Name, byte[] Bytes);

/// <summary>
/// Carries a book folder's images (its cover, fetched or read out of its files, and images under other names, such as a
/// <c>folder.jpg</c>) over to the folder a relink places the book in, so the cover isn't fetched again and the old folder
/// doesn't stay behind for an image Fetchshelf left in it.
/// </summary>
public static class FolderImages
{
    /// <summary>
    /// Reads the JPEG, PNG and WebP files directly in <paramref name="folder"/> into memory and deletes them, leaving the
    /// book's own linked files (<paramref name="linkedFiles"/>) alone. None when the folder is gone.
    /// </summary>
    public static List<FolderImage> Take(string folder, IEnumerable<LinkedFileRecord> linkedFiles)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var linked = linkedFiles.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var images = new List<FolderImage>();
        foreach (var path in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).ToList())
        {
            var name = Path.GetFileName(path);
            if (!CoverFile.IsImage(name) || linked.Contains(name))
            {
                continue;
            }

            images.Add(new FolderImage(name, File.ReadAllBytes(path)));
            File.Delete(path);
        }

        return images;
    }

    /// <summary>
    /// Writes <paramref name="images"/> into <paramref name="folder"/> when it exists. Each keeps its name and is skipped
    /// when a file of that name is there; a Fetchshelf cover (<c>cover.jpg</c>, <c>.png</c>, <c>.webp</c>) is also skipped
    /// when the folder had an image already, such as another book's cover. Returns the names written.
    /// </summary>
    public static List<string> Put(string folder, IEnumerable<FolderImage> images)
    {
        var written = new List<string>();
        if (!Directory.Exists(folder))
        {
            return written;
        }

        var hadImage = !CoverFile.IsNeeded(Directory.EnumerateFiles(folder).Select(Path.GetFileName)!);
        foreach (var image in images)
        {
            var path = Path.Combine(folder, image.Name);
            if (File.Exists(path) || (hadImage && CoverFile.Names.Contains(image.Name, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            File.WriteAllBytes(path, image.Bytes);
            written.Add(image.Name);
        }

        return written;
    }
}
