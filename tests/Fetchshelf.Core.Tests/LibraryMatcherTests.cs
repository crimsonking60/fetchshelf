using Fetchshelf.Core.Metadata;
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

    [Fact]
    public void FindMatchingLibraryIndex_DownloadDirIsOnlyAStringPrefixOfTheSavePath_GoesToTheLibraryThatHoldsIt()
    {
        IReadOnlyList<LibraryMatchRule> libraries =
        [
            new LibraryMatchRule { DownloadDir = "/data/audio" },
            new LibraryMatchRule { DownloadDir = "/data/audiobooks2" },
        ];

        var index = LibraryMatcher.FindMatchingLibraryIndex(libraries, null, "/data/audiobooks2/book", []);

        Assert.Equal(1, index);
    }

    private static TorrentMetadata Book(FictionCategory? fiction, params GenreCategory[] genres) => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Ebook,
        FictionCategory = fiction,
        LegacyCategory = LegacyCategory.EbookHistory,
        GenreCategories = genres.ToHashSet(),
        Language = Language.English,
        Title = "A Book",
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void CandidateIndexes_EveryLibraryTheCategoryGives_InOrder_WhateverTheirGenres()
    {
        LibraryMatchRule[] libraries =
        [
            new() { Category = "ebooks", Genres = new(new HashSet<GenreCategory> { GenreCategory.Historical }, new HashSet<GenreCategory>(), FictionCategory.Nonfiction) },
            new() { Category = "audiobooks" },
            new() { Category = "ebooks" },
        ];

        Assert.Equal([0, 2], LibraryMatcher.CandidateIndexes(libraries, "ebooks", "/downloads/book", []));
        Assert.Equal(0, LibraryMatcher.FindMatchingLibraryIndex(libraries, "ebooks", "/downloads/book", []));
    }

    [Fact]
    public void ChooseForBook_TheFirstCandidateWhoseGenresTakeIt_ElseNone()
    {
        LibraryMatchRule[] libraries =
        [
            new() { Category = "ebooks", Genres = new(new HashSet<GenreCategory> { GenreCategory.Historical }, new HashSet<GenreCategory>(), FictionCategory.Nonfiction) },
            new() { Category = "ebooks", Genres = new(new HashSet<GenreCategory>(), new HashSet<GenreCategory> { GenreCategory.Romance }, null) },
        ];

        Assert.Equal(0, LibraryMatcher.ChooseForBook(libraries, [0, 1], Book(FictionCategory.Nonfiction, GenreCategory.Historical)));
        Assert.Equal(1, LibraryMatcher.ChooseForBook(libraries, [0, 1], Book(FictionCategory.Fiction, GenreCategory.Historical)));
        Assert.Null(LibraryMatcher.ChooseForBook(libraries, [0, 1], Book(FictionCategory.Fiction, GenreCategory.Romance)));
        Assert.Null(LibraryMatcher.ChooseForBook(libraries, [], Book(null)));
    }
}
