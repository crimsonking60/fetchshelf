namespace Fetchshelf.Core.Organizer;

/// <summary>Format preference ranking, Fetchshelf-spec.md §7.6.</summary>
public static class FormatRanker
{
    /// <summary>Rank assigned when no extension from the preference list is present.</summary>
    public const int NotPresent = int.MaxValue;

    /// <summary>
    /// A torrent's rank is the position of the first listed extension present among its file types
    /// (lower is better; none present ranks worst).
    /// </summary>
    public static int Rank(IReadOnlyList<string> preferenceList, IEnumerable<string> fileExtensions)
    {
        var present = new HashSet<string>(
            fileExtensions.Select(e => e.TrimStart('.')), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < preferenceList.Count; i++)
        {
            if (present.Contains(preferenceList[i]))
            {
                return i;
            }
        }

        return NotPresent;
    }

    /// <summary>
    /// The best-ranked (lowest-rank) extension from <paramref name="preferenceList"/> present among
    /// <paramref name="fileExtensions"/>, case-insensitively, or null if none are present.
    /// </summary>
    public static string? SelectBestExtension(IReadOnlyList<string> preferenceList, IEnumerable<string> fileExtensions)
    {
        var present = new HashSet<string>(
            fileExtensions.Select(e => e.TrimStart('.')), StringComparer.OrdinalIgnoreCase);

        return preferenceList.FirstOrDefault(present.Contains);
    }
}
