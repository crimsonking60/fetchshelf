using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class DuplicateCopiesTests
{
    private sealed record Copy(string Id, TorrentMetadata Metadata, int FormatRank = 0, long LinkedSize = 0);

    private static TorrentMetadata Book(string title, string author = "Jane Doe") => new()
    {
        MamId = 1,
        VipStatus = VipStatus.NotVip,
        MediaType = MediaType.Audiobook,
        LegacyCategory = LegacyCategory.AudiobookGeneralFiction,
        Language = Language.English,
        Title = title,
        Authors = [author],
        MetadataSource = MetadataSource.Mam,
        UploadTime = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void GroupBySameBook_TwoCopiesOfOneBookAndAnotherBook_GroupsTheCopiesInOrder()
    {
        Copy[] copies = [new("a", Book("Dune", "Frank Herbert")), new("b", Book("Emma", "Jane Austen")), new("c", Book("Dune", "Frank Herbert"))];

        var groups = DuplicateCopies.GroupBySameBook(copies, c => c.Metadata);

        Assert.Equal([["a", "c"], ["b"]], groups.Select(g => g.Select(c => c.Id).ToList()).ToList());
    }

    [Fact]
    public void GroupBySameBook_DifferentTitlesBySameAuthor_KeepsThemApart()
    {
        Copy[] copies = [new("a", Book("The Way of Kings")), new("b", Book("Words of Radiance"))];

        var groups = DuplicateCopies.GroupBySameBook(copies, c => c.Metadata);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void GroupBySameBook_InterleavedTitles_KeepsGroupsInOrderOfTheirFirstCopy()
    {
        Copy[] copies =
        [
            new("a", Book("Emma", "Jane Austen")), new("b", Book("Dune", "Frank Herbert")), new("c", Book("Dune", "Someone Else")),
            new("d", Book("Dune", "Someone Else")), new("e", Book("Emma", "Jane Austen")), new("f", Book("Dune", "Frank Herbert")),
        ];

        var groups = DuplicateCopies.GroupBySameBook(copies, c => c.Metadata);

        Assert.Equal([["a", "e"], ["b", "f"], ["c", "d"]], groups.Select(g => g.Select(c => c.Id).ToList()).ToList());
    }

    [Fact]
    public void GroupBySameBook_TitlesDifferOnlyInCaseAndArticle_GroupsThem()
    {
        Copy[] copies = [new("a", Book("The Way of Kings")), new("b", Book("way of kings"))];

        var groups = DuplicateCopies.GroupBySameBook(copies, c => c.Metadata);

        Assert.Equal([["a", "b"]], groups.Select(g => g.Select(c => c.Id).ToList()).ToList());
    }

    [Fact]
    public void AreCopiesOfSameBook_TitlesDifferOnlyInCaseAndArticle_ReturnsTrue() =>
        Assert.True(DuplicateCopies.AreCopiesOfSameBook(Book("The Way of Kings"), Book("way of kings")));

    [Fact]
    public void GroupBySameBook_SameTitleDifferentAuthor_KeepsThemApart()
    {
        Copy[] copies = [new("a", Book("Dune", "Frank Herbert")), new("b", Book("Dune", "Someone Else"))];

        var groups = DuplicateCopies.GroupBySameBook(copies, c => c.Metadata);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void PickKeeper_DifferentFormatRanks_KeepsTheBestRankedEvenIfSmaller()
    {
        Copy[] copies = [new("mp3", Book("Dune"), FormatRank: 1, LinkedSize: 900), new("m4b", Book("Dune"), FormatRank: 0, LinkedSize: 100)];

        var keeper = DuplicateCopies.PickKeeper(copies, c => c.FormatRank, c => c.LinkedSize);

        Assert.Equal("m4b", keeper.Id);
    }

    [Fact]
    public void PickKeeper_EqualFormatRanks_KeepsTheLargerLinkedCopy()
    {
        Copy[] copies = [new("small", Book("Dune"), LinkedSize: 100), new("large", Book("Dune"), LinkedSize: 900)];

        var keeper = DuplicateCopies.PickKeeper(copies, c => c.FormatRank, c => c.LinkedSize);

        Assert.Equal("large", keeper.Id);
    }

    [Fact]
    public void PickKeeper_FullTie_KeepsTheEarlierCopy()
    {
        Copy[] copies = [new("first", Book("Dune")), new("second", Book("Dune"))];

        var keeper = DuplicateCopies.PickKeeper(copies, c => c.FormatRank, c => c.LinkedSize);

        Assert.Equal("first", keeper.Id);
    }

    [Fact]
    public void GroupBySameBook_TwoDifferentRussianBooksBySameAuthor_KeepsThemApart()
    {
        var groups = DuplicateCopies.GroupBySameBook([Book("Война и мир", "Лев Толстой"), Book("Анна Каренина", "Лев Толстой")], b => b);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void GroupBySameBook_TwoCopiesOfOneRussianBook_GroupsThem()
    {
        var groups = DuplicateCopies.GroupBySameBook([Book("Война и мир", "Лев Толстой"), Book("война и мир", "Лев Толстой")], b => b);

        Assert.Single(groups);
    }

    [Fact]
    public void GroupBySameBook_AuthorSpelledDifferently_GroupsTheCopies()
    {
        var groups = DuplicateCopies.GroupBySameBook([Book("The Hobbit", "J. R. R. Tolkien"), Book("The Hobbit", "J.R.R. Tolkien")], b => b);

        Assert.Single(groups);
    }

    [Fact]
    public void GroupBySameBook_BlankTitles_AreNeverGrouped()
    {
        var groups = DuplicateCopies.GroupBySameBook([Book(""), Book(" ")], b => b);

        Assert.Equal(2, groups.Count);
        Assert.False(DuplicateCopies.AreCopiesOfSameBook(Book(""), Book("")));
    }
}
