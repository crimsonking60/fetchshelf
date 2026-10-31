namespace Fetchshelf.Core.Organizer;

/// <summary>Library mismatch states for an already-linked torrent.</summary>
public enum LibraryMismatchState
{
    None,
    NoLibrary,
    NewLibraryDir,
    NewPath,
}

/// <summary>Computes a linked torrent's library mismatch state.</summary>
public static class LibraryMismatchCalculator
{
    /// <summary>
    /// <paramref name="recomputedLibraryDir"/>/<paramref name="recomputedFullPath"/> are null when no
    /// library currently matches the torrent.
    /// </summary>
    public static LibraryMismatchState Compute(
        string storedLibraryPath, string? recomputedLibraryDir, string? recomputedFullPath)
    {
        if (recomputedLibraryDir is null || recomputedFullPath is null)
        {
            return LibraryMismatchState.NoLibrary;
        }

        if (!storedLibraryPath.StartsWith(recomputedLibraryDir, StringComparison.Ordinal))
        {
            return LibraryMismatchState.NewLibraryDir;
        }

        return string.Equals(storedLibraryPath, recomputedFullPath, StringComparison.Ordinal)
            ? LibraryMismatchState.None
            : LibraryMismatchState.NewPath;
    }
}
