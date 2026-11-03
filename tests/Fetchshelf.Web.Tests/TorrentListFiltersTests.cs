using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Fetchshelf.Web.Pages.Torrents;

namespace Fetchshelf.Web.Tests;

public class TorrentListFiltersTests
{
    private static TorrentEntity Torrent(string id = "hash1") => new()
    {
        Id = id,
        MamId = 1,
        NormalizedTitleKey = "book",
        Title = "Book",
        MetadataJson = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void StatusOf_TellsTheCasesApart()
    {
        var linked = Torrent();
        linked.LibraryPath = "/library/Author/Book";
        var unlinked = Torrent();
        unlinked.UnlinkedAt = DateTimeOffset.UtcNow;
        var replaced = Torrent();
        replaced.ReplacedWithTorrentId = "hash2";

        Assert.Equal(TorrentLinkStatus.Linked, TorrentListFilters.StatusOf(linked));
        Assert.Equal(TorrentLinkStatus.Unlinked, TorrentListFilters.StatusOf(unlinked));
        Assert.Equal(TorrentLinkStatus.Replaced, TorrentListFilters.StatusOf(replaced));
        Assert.Equal(TorrentLinkStatus.Waiting, TorrentListFilters.StatusOf(Torrent()));
        Assert.Equal(TorrentLinkStatus.MetadataOnly, TorrentListFilters.StatusOf(Torrent("mam:123")));
    }

    [Fact]
    public void StatusOf_ATorrentWithALibraryFolder_IsLinked_WhateverItsMarkersSay()
    {
        var torrent = Torrent();
        torrent.LibraryPath = "/library/Author/Book";
        torrent.ReplacedWithTorrentId = "hash2";

        Assert.Equal(TorrentLinkStatus.Linked, TorrentListFilters.StatusOf(torrent));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("unknown", true)]
    [InlineData("not-linked", true)]
    [InlineData("replaced", true)]
    [InlineData("linked", false)]
    [InlineData("waiting", false)]
    public void MatchesStatus_ForAReplacedTorrent(string? status, bool expected)
    {
        var torrent = Torrent();
        torrent.ReplacedWithTorrentId = "hash2";

        Assert.Equal(expected, TorrentListFilters.MatchesStatus(torrent, status));
    }

    [Fact]
    public void MatchesProblem_FindsMismatchesAndTorrentsMissingFromQBittorrent()
    {
        var mismatched = Torrent();
        mismatched.LibraryMismatchState = "NewPath";
        var missing = Torrent();
        missing.ClientStatus = ClientStatuses.NotInClient;
        var fine = Torrent();
        fine.ClientStatus = "uploading";

        Assert.True(TorrentListFilters.MatchesProblem(mismatched, "mismatch"));
        Assert.False(TorrentListFilters.MatchesProblem(mismatched, "not-in-client"));
        Assert.True(TorrentListFilters.MatchesProblem(missing, "not-in-client"));
        Assert.True(TorrentListFilters.MatchesProblem(missing, "any"));
        Assert.True(TorrentListFilters.MatchesProblem(mismatched, "any"));
        Assert.False(TorrentListFilters.MatchesProblem(fine, "any"));
        Assert.True(TorrentListFilters.MatchesProblem(fine, null));
    }
}
