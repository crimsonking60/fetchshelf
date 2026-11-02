namespace Fetchshelf.Core.Organizer;

/// <summary>Which of a torrent's files are linked: only those in its best audio format and its best ebook format.</summary>
public static class LinkedFormats
{
    /// <summary>The best-ranked audio and ebook extension among the file names (see <see cref="FormatRanker"/>), each null when none is listed.</summary>
    public static (string? Audio, string? Ebook) Choose(
        IReadOnlyList<string> audioTypes, IReadOnlyList<string> ebookTypes, IEnumerable<string> fileNames)
    {
        var extensions = fileNames.Select(Path.GetExtension).OfType<string>().ToList();
        return (FormatRanker.SelectBestExtension(audioTypes, extensions), FormatRanker.SelectBestExtension(ebookTypes, extensions));
    }

    /// <summary>Whether a file has one of the chosen extensions, ignoring case.</summary>
    public static bool IsChosen(string fileName, string? audio, string? ebook) =>
        HasExtension(fileName, audio) || HasExtension(fileName, ebook);

    private static bool HasExtension(string fileName, string? extension) =>
        extension is not null && string.Equals(Path.GetExtension(fileName).TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase);
}
