namespace Fetchshelf.Data.Opds;

/// <summary>
/// The cover image in a book's own folder: the one the Link job writes with `covers_from_mam`, the one a split
/// collection's book brings from the torrent, or one put there by hand or by Audiobookshelf.
/// </summary>
public static class FolderCover
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>
    /// The folder's cover: an image named <c>cover</c> (any case), else one named <c>folder</c>, else its only image;
    /// null when there is none, several without either name, or the folder isn't there. Sub-folders aren't looked in.
    /// </summary>
    public static FileInfo? Find(string bookFolder)
    {
        List<FileInfo> images;
        try
        {
            var directory = new DirectoryInfo(bookFolder);
            if (!directory.Exists)
            {
                return null;
            }

            images = directory.EnumerateFiles()
                .Where(file => ImageExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        FileInfo? Named(string name) =>
            images.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
        return Named("cover") ?? Named("folder") ?? (images.Count == 1 ? images[0] : null);
    }

    /// <summary>The image at <paramref name="path"/>, or null when it is too large or not an image Fetchshelf knows.</summary>
    public static BookCover? Read(string path)
    {
        if (new FileInfo(path).Length > BookCoverExtractor.MaxCoverBytes)
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        return BookCoverExtractor.SniffImageType(bytes) is { } contentType ? new BookCover(bytes, contentType) : null;
    }
}
