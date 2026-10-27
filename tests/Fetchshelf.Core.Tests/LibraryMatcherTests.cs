using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LibraryMatcherTests
{
    [Fact]
    public void FindMatchingLibraryIndex_TwoLibrariesMatch_FirstWins()
    {
        IReadOnlyList<LibraryMatchRule> libraries =
        [
            new LibraryMatchRule { Category = "audiobooks" },
            new LibraryMatchRule { Category = "audiobooks" },
        ];

        var index = LibraryMatcher.FindMatchingLibraryIndex(libraries, "audiobooks", "/downloads/book", []);

        Assert.Equal(0, index);
    }

    [Fact]
    public void FindMatchingLibraryIndex_DenyTagPresent_RejectsLibrary()
    {
        IReadOnlyList<LibraryMatchRule> libraries =
        [
            new LibraryMatchRule { Category = "audiobooks", DenyTags = ["dnp"] },
        ];

        var index = LibraryMatcher.FindMatchingLibraryIndex(libraries, "audiobooks", "/downloads/book", ["dnp"]);

        Assert.Null(index);
    }

    [Fact]
    public void FindMatchingLibraryIndex_AllowTagRequiredButAbsent_RejectsLibrary()
    {
        IReadOnlyList<LibraryMatchRule> libraries =
        [
            new LibraryMatchRule { Category = "audiobooks", AllowTags = ["fiction"] },
        ];

        var index = LibraryMatcher.FindMatchingLibraryIndex(libraries, "audiobooks", "/downloads/book", ["nonfiction"]);

        Assert.Null(index);
    }

    [Fact]
    public void FindMatchingLibraryIndex_DownloadDirPrefixMatches_ReturnsLibrary()
    {
        IReadOnlyList<LibraryMatchRule> libraries =
        [
            new LibraryMatchRule { DownloadDir = "/downloads/audio" },
        ];

        var index = LibraryMatcher.FindMatchingLibraryIndex(libraries, null, "/downloads/audio/book1", []);

        Assert.Equal(0, index);
    }

    [Fact]
    public void FindMatchingLibraryIndex_NoLibraryMatches_ReturnsNull()
    {
        IReadOnlyList<LibraryMatchRule> libraries =
        [
            new LibraryMatchRule { Category = "ebooks" },
        ];

        var index = LibraryMatcher.FindMatchingLibraryIndex(libraries, "audiobooks", "/downloads/book", []);

        Assert.Null(index);
    }
}
