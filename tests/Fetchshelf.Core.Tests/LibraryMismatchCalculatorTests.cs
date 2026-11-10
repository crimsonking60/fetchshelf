using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LibraryMismatchCalculatorTests
{
    [Fact]
    public void Compute_NoLibraryMatchesAnyMore_ReturnsNoLibrary()
    {
        var state = LibraryMismatchCalculator.Compute("/library/author/title", null, null);

        Assert.Equal(LibraryMismatchState.NoLibrary, state);
    }

    [Fact]
    public void Compute_RecomputedUnderDifferentLibraryDir_ReturnsNewLibraryDir()
    {
        var state = LibraryMismatchCalculator.Compute(
            "/library-a/author/title", "/library-b", "/library-b/author/title");

        Assert.Equal(LibraryMismatchState.NewLibraryDir, state);
    }

    [Fact]
    public void Compute_SameLibraryDirDifferentPath_ReturnsNewPath()
    {
        var state = LibraryMismatchCalculator.Compute(
            "/library/author/old-title", "/library", "/library/author/new-title");

        Assert.Equal(LibraryMismatchState.NewPath, state);
    }

    [Fact]
    public void Compute_PathUnchanged_ReturnsNone()
    {
        var state = LibraryMismatchCalculator.Compute(
            "/library/author/title", "/library", "/library/author/title");

        Assert.Equal(LibraryMismatchState.None, state);
    }

    [Fact]
    public void Compute_NarratorExclusionFlipAlone_DoesNotCountAsMismatch()
    {
        // Represents the "flipped exclude_narrator_in_library_dir alone" case: the caller recomputes
        // the folder with the new setting, but since the torrent has no narrators the folder name is
        // unaffected, so the path is unchanged and this still returns None.
        var state = LibraryMismatchCalculator.Compute(
            "/library/author/title", "/library", "/library/author/title");

        Assert.Equal(LibraryMismatchState.None, state);
    }

    [Fact]
    public void Compute_StoredInASiblingFolderWithALongerName_ReturnsNewLibraryDir()
    {
        var state = LibraryMismatchCalculator.Compute(
            "/library/audiobooks/author/title", "/library/audio", "/library/audio/author/title");

        Assert.Equal(LibraryMismatchState.NewLibraryDir, state);
    }
}
