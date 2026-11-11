using Fetchshelf.Core.AutoDownloader;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public class LibraryCopyRankTests
{
    private static readonly FormatPreferenceLists Lists = new()
    {
        AudioTypes = ["m4b", "mp3"],
        EbookTypes = ["epub", "pdf"],
        MusicTypes = ["flac", "mp3"],
        RadioTypes = ["mp3"],
        Libraries = new Dictionary<string, LibraryFormatLists> { ["Mp3 First"] = new(["mp3", "m4b"], null) },
    };

    private static TorrentEntity Copy(string? audio = null, string? ebook = null, string mediaType = "Audiobook", bool linked = true) => new()
    {
        Id = "hash",
        MamId = 1,
        Title = "A Book",
        NormalizedTitleKey = "a book",
        MetadataJson = "{}",
        CreatedAt = DateTimeOffset.UnixEpoch,
        ChosenAudioExtension = audio,
        ChosenEbookExtension = ebook,
        MediaType = mediaType,
        LibraryPath = linked ? "/library/A Book" : null,
    };

    [Theory]
    [InlineData("m4b", null, "Audiobook", 0, false)]
    [InlineData("mp3", null, "Audiobook", 1, true)]
    [InlineData(null, "pdf", "Ebook", 1, true)]
    [InlineData(null, "epub", "Ebook", 0, false)]
    [InlineData("mp3", null, "Musicology", 1, true)]
    [InlineData("flac", null, "Musicology", 0, false)]
    [InlineData("ogg", null, "Audiobook", FormatRanker.NotPresent, true)]
    public void RankAndCouldBeUpgraded_UseTheCopysChosenFormatAndItsList(string? audio, string? ebook, string mediaType, int rank, bool upgradable)
    {
        var copy = Copy(audio, ebook, mediaType);

        Assert.Equal(rank, LibraryCopyRank.Of(copy, Lists));
        Assert.Equal(upgradable, LibraryCopyRank.CouldBeUpgraded(copy, Lists));
    }

    [Fact]
    public void CouldBeUpgraded_NeverForACopyWithNothingChosen_NotLinked_OrReplaced()
    {
        Assert.Equal(FormatRanker.NotPresent, LibraryCopyRank.Of(Copy(), Lists));
        Assert.False(LibraryCopyRank.CouldBeUpgraded(Copy(), Lists));
        Assert.False(LibraryCopyRank.CouldBeUpgraded(Copy("mp3", linked: false), Lists));
        var replaced = Copy("mp3");
        replaced.ReplacedWithTorrentId = "other";
        Assert.False(LibraryCopyRank.CouldBeUpgraded(replaced, Lists));
    }

    [Fact]
    public void ACopyInALibraryWithItsOwnList_IsRankedByThatList()
    {
        var mp3 = Copy("mp3");
        mp3.LinkerName = "Mp3 First";
        var ebook = Copy(ebook: "pdf", mediaType: "Ebook");
        ebook.LinkerName = "Mp3 First";

        Assert.Equal(0, LibraryCopyRank.Of(mp3, Lists));
        Assert.False(LibraryCopyRank.CouldBeUpgraded(mp3, Lists));
        Assert.Equal("mp3", LibraryCopyRank.BestFormat(mp3, Lists));
        // The library sets no ebook list, so the global one ranks its ebooks.
        Assert.True(LibraryCopyRank.CouldBeUpgraded(ebook, Lists));
    }

    [Fact]
    public void BestFormat_IsTheFirstInTheCopysList() =>
        Assert.Equal(("m4b", "epub", null), (LibraryCopyRank.BestFormat(Copy("mp3"), Lists), LibraryCopyRank.BestFormat(Copy(ebook: "pdf"), Lists), LibraryCopyRank.BestFormat(Copy(), Lists)));
}
