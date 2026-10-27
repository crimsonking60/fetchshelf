using Fetchshelf.Core.Metadata;

namespace Fetchshelf.Core.Organizer;

/// <summary>Computes a torrent's destination library folder, Fetchshelf-spec.md §8.4.</summary>
public static class FolderNameComputer
{
    /// <summary>
    /// Computes the full destination folder under <paramref name="libraryDir"/>.
    /// <paramref name="directoryExists"/> is consulted only for the narrator-exclusion collision
    /// fallback, so this remains a pure function of its inputs (no direct file-system access).
    /// </summary>
    public static string Compute(
        string libraryDir,
        TorrentMetadata metadata,
        bool excludeNarratorInLibraryDir,
        Func<string, bool> directoryExists)
    {
        var author = metadata.Authors.Count > 0 ? metadata.Authors[0] : string.Empty;
        var series = SelectSeries(metadata.Series);

        var titleComponent = BuildTitleComponent(metadata, series);
        var titleWithEdition = metadata.Edition is { } edition
            ? $"{titleComponent}, {edition.Label}"
            : titleComponent;

        string finalTitleComponent;
        if (metadata.Narrators.Count == 0)
        {
            finalTitleComponent = titleWithEdition;
        }
        else if (!excludeNarratorInLibraryDir)
        {
            finalTitleComponent = WithNarrator(titleWithEdition, metadata.Narrators[0]);
        }
        else
        {
            var narratorlessPath = JoinSanitized(libraryDir, BuildComponents(author, series?.Name, titleWithEdition));
            finalTitleComponent = directoryExists(narratorlessPath)
                ? WithNarrator(titleWithEdition, metadata.Narrators[0])
                : titleWithEdition;
        }

        return JoinSanitized(libraryDir, BuildComponents(author, series?.Name, finalTitleComponent));
    }

    private static string WithNarrator(string titleComponent, string narrator) => $"{titleComponent} {{{narrator}}}";

    private static Series? SelectSeries(IReadOnlyList<Series> series) =>
        series.FirstOrDefault(s => s.Entries.Count > 0) ?? series.FirstOrDefault();

    private static string BuildTitleComponent(TorrentMetadata metadata, Series? series)
    {
        if (series is null || series.Entries.Count == 0)
        {
            return metadata.Title;
        }

        var entriesDisplay = string.Join(", ", series.SortedEntries.Select(e => e.Display));
        return $"{series.Name} #{entriesDisplay} - {metadata.Title}";
    }

    private static List<string> BuildComponents(string author, string? seriesName, string titleComponent)
    {
        var components = new List<string> { author };
        if (seriesName is not null)
        {
            components.Add(seriesName);
        }

        components.Add(titleComponent);
        return components;
    }

    private static string JoinSanitized(string libraryDir, IEnumerable<string> components) =>
        Path.Combine([libraryDir, .. components.Select(Sanitize)]);

    private static string Sanitize(string component)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(component.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }
}
