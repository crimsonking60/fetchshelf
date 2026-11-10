using System.Text.RegularExpressions;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Computes where a torrent's chosen files land inside its destination folder: a
/// disc-numbered source sub-folder is preserved as `Disc N`; any other source structure is flattened.
/// </summary>
public static partial class LinkFilePlacer
{
    public static string DestinationPath(string destinationFolder, string sourceRelativeFilePath)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceRelativeFilePath.Replace('\\', '/')) ?? string.Empty;
        var discNumber = FindDiscNumber(sourceDirectory);
        var fileName = Path.GetFileName(sourceRelativeFilePath);

        return discNumber is null
            ? Path.Combine(destinationFolder, fileName)
            : Path.Combine(destinationFolder, $"Disc {discNumber}", fileName);
    }

    /// <summary>
    /// The destinations of all of a torrent's chosen files, in the same order. Each file gets
    /// <see cref="DestinationPath"/>, so a torrent without clashes is placed exactly as one file at a time would be.
    /// Files that would land on the same name (ignoring case, as a library read over SMB or from Windows or macOS
    /// would) keep their source sub-folders instead, put in front of the file name: <c>Part 1/01.mp3</c> becomes
    /// <c>Part 1 - 01.mp3</c>. That keeps one folder per book and sorts by part, then track. The torrent's own top
    /// folder is left out, and a disc folder still becomes a <c>Disc N</c> folder. Anything still clashing after that
    /// gets <c> (2)</c>, <c> (3)</c> before its extension.
    /// </summary>
    public static IReadOnlyList<string> DestinationPaths(string destinationFolder, IReadOnlyList<string> sourceRelativeFilePaths)
    {
        var segments = sourceRelativeFilePaths.Select(path => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var topFolderLength = HasCommonTopFolder(segments) ? 1 : 0;

        var destinations = sourceRelativeFilePaths.Select(path => DestinationPath(destinationFolder, path)).ToList();
        var clashing = destinations
            .Select((destination, index) => (destination, index))
            .GroupBy(d => d.destination, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(d => d.index))
            .ToList();

        foreach (var index in clashing)
        {
            var folders = segments[index][topFolderLength..^1].Where(folder => !DiscSegmentPattern().IsMatch(folder)).ToList();
            if (folders.Count == 0)
            {
                continue;
            }

            var destination = destinations[index];
            var prefixed = $"{string.Join(" - ", folders)} - {Path.GetFileName(destination)}";
            destinations[index] = Path.Combine(Path.GetDirectoryName(destination) ?? destinationFolder, prefixed);
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < destinations.Count; i++)
        {
            var destination = destinations[i];
            for (var number = 2; !taken.Add(destination); number++)
            {
                destination = FileLinker.NumberedName(destinations[i], number);
            }

            destinations[i] = destination;
        }

        return destinations;
    }

    /// <summary>Whether every file sits under one folder: the torrent's own, named after it, which says nothing about the file.</summary>
    private static bool HasCommonTopFolder(IReadOnlyList<string[]> segments) =>
        segments.Count > 0
        && segments.All(parts => parts.Length > 1)
        && segments.All(parts => string.Equals(parts[0], segments[0][0], StringComparison.Ordinal));

    private static string? FindDiscNumber(string sourceDirectory)
    {
        foreach (var segment in sourceDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DiscSegmentPattern().Match(segment);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    [GeneratedRegex(@"(?:CD|Disc|Disk)\s*0*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DiscSegmentPattern();
}
