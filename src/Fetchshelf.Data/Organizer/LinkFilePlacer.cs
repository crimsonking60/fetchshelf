using System.Text.RegularExpressions;

namespace Fetchshelf.Data.Organizer;

/// <summary>
/// Computes where a chosen file lands inside a torrent's destination folder: a
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
