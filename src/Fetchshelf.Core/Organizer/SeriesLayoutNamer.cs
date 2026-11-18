using System.Text;
using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Where the series layout puts a book: the folder, and the name of its file without the extension.</summary>
/// <param name="IsOneShot">Whether the book has no series, so it goes into the one-shots folder.</param>
public sealed record SeriesPlacement(string Folder, string FileBaseName, bool IsOneShot);

/// <summary>
/// Names a book's place in a library with <c>layout = "series"</c>, the shape comic servers such as Komga read: one folder
/// per series under <c>library_dir</c>, each book one file in it named <c>Series #001 - Title, Edition</c>, and books
/// without a series as <c>Title, Edition</c> in the one-shots folder. The names are cleaned and shortened as
/// <c>folder_template</c>'s are (<see cref="FolderLayout.RenderName"/>). Numbers are padded to three digits so the names
/// sort in series order. Narrators are left out: they don't apply to comics.
/// </summary>
public static class SeriesLayoutNamer
{
    private const string FolderTemplate = "{series}";
    private const string FileTemplate = "{series}[ #{series_number}] - {title}[, {edition}]";
    private const string OneShotFileTemplate = "{title}[, {edition}]";

    /// <param name="libraryDir">The library's folder.</param>
    /// <param name="oneshotsDir">The folder under it that books without a series go into.</param>
    /// <param name="metadata">The book.</param>
    /// <param name="rules">The characters the names may hold.</param>
    /// <param name="extension">The extension the file name gets, such as <c>.cbz</c>: the name leaves room for it.</param>
    /// <param name="markLanguage">Whether the file name ends with the book's language (<see cref="FolderNameComputer.LanguageMark"/>).</param>
    public static SeriesPlacement Compute(
        string libraryDir, string oneshotsDir, TorrentMetadata metadata, FileNameRules rules, string extension = "", bool markLanguage = false)
    {
        var series = FolderNameComputer.SelectSeries(metadata.Series);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["series"] = series?.Name ?? string.Empty,
            ["series_number"] = series is null ? string.Empty : string.Join(", ", series.SortedEntries.Select(Padded)),
            ["title"] = metadata.Title,
            ["edition"] = metadata.Edition?.Label ?? string.Empty,
        };
        var reserve = Encoding.UTF8.GetByteCount(extension);
        var mark = markLanguage ? FolderNameComputer.LanguageMark(metadata.Language) : string.Empty;
        var folder = series is null ? string.Empty : FolderLayout.RenderName(FolderTemplate, values, rules);
        return folder.Length == 0
            ? new SeriesPlacement(Path.Combine(libraryDir, oneshotsDir), FolderLayout.RenderName(OneShotFileTemplate, values, rules, reserve, mark), IsOneShot: true)
            : new SeriesPlacement(Path.Combine(libraryDir, folder), FolderLayout.RenderName(FileTemplate, values, rules, reserve, mark), IsOneShot: false);
    }

    /// <summary>
    /// An entry with each book number's whole part padded to three digits: <c>1</c> → <c>001</c>, <c>1.5</c> →
    /// <c>001.5</c>, <c>1-6</c> → <c>001-006</c>, the part <c>2p1</c> → <c>002p1</c>. Numbers of 1000 and up stay as they are.
    /// </summary>
    public static string Padded(SeriesEntry entry) => entry.Kind switch
    {
        SeriesEntryKind.Range when entry.Display.IndexOf('-', 1) is var dash and > 0 => $"{Pad(entry.Display[..dash])}-{Pad(entry.Display[(dash + 1)..])}",
        SeriesEntryKind.Part when entry.Display.IndexOf('p', StringComparison.OrdinalIgnoreCase) is var p and > 0 => $"{Pad(entry.Display[..p])}{entry.Display[p..]}",
        _ => Pad(entry.Display),
    };

    private static string Pad(string number)
    {
        var dot = number.IndexOf('.', StringComparison.Ordinal);
        var whole = dot < 0 ? number : number[..dot];
        if (!whole.All(char.IsAsciiDigit))
        {
            return number;
        }

        return whole.PadLeft(3, '0') + (dot < 0 ? string.Empty : number[dot..]);
    }
}
