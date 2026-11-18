using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Computes a torrent's destination library folder.</summary>
public static class FolderNameComputer
{
    /// <summary>
    /// Computes the full destination folder under <paramref name="libraryDir"/>, laid out by <paramref name="layout"/>
    /// (<see cref="FolderLayout.Default"/> when null) with names cleaned by <paramref name="rules"/>.
    /// <paramref name="directoryExists"/> is consulted only for the narrator-exclusion collision
    /// fallback, so this remains a pure function of its inputs (no direct file-system access).
    /// </summary>
    public static string Compute(
        string libraryDir,
        TorrentMetadata metadata,
        bool excludeNarratorInLibraryDir,
        Func<string, bool> directoryExists,
        FolderLayout? layout = null,
        FileNameRules rules = FileNameRules.Posix)
    {
        layout ??= FolderLayout.Default;
        var values = Values(metadata);
        string Path(IReadOnlyDictionary<string, string> tokens) =>
            System.IO.Path.Combine([libraryDir, .. layout.Render(tokens, rules)]);

        if (excludeNarratorInLibraryDir && metadata.Narrators.Count > 0)
        {
            var narratorless = Path(new Dictionary<string, string>(values) { ["narrator"] = string.Empty });
            if (!directoryExists(narratorless))
            {
                return narratorless;
            }
        }

        return Path(values);
    }

    /// <summary>Each <see cref="FolderLayout.Tokens"/> token's value for <paramref name="metadata"/>, empty when the book has none.</summary>
    public static Dictionary<string, string> Values(TorrentMetadata metadata)
    {
        var series = SelectSeries(metadata.Series);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["author"] = metadata.Authors.Count > 0 ? metadata.Authors[0] : string.Empty,
            ["authors"] = string.Join(", ", metadata.Authors),
            ["series"] = series?.Name ?? string.Empty,
            ["series_number"] = series is null ? string.Empty : string.Join(", ", series.SortedEntries.Select(e => e.Display)),
            ["title"] = metadata.Title,
            ["edition"] = metadata.Edition?.Label ?? string.Empty,
            ["narrator"] = metadata.Narrators.Count > 0 ? metadata.Narrators[0] : string.Empty,
            ["language"] = LanguageNameResolver.DisplayName(metadata.Language),
            ["year"] = metadata.UploadTime.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The series a book is placed by: the first with entries, else the first; null without one.</summary>
    public static Series? SelectSeries(IReadOnlyList<Series> series) =>
        series.FirstOrDefault(s => s.Entries.Count > 0) ?? series.FirstOrDefault();
}
