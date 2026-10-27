using Fetchshelf.Web.Pages.Torrents;

namespace Fetchshelf.Web.Tests;

public class QBittorrentStateLabelTests
{
    [Theory]
    [InlineData("stalledUP", "Seeding")]
    [InlineData("uploading", "Seeding")]
    [InlineData("pausedUP", "Stopped")]
    [InlineData("stalledDL", "Downloading (stalled)")]
    [InlineData("checkingResumeData", "Checking")]
    [InlineData("missingFiles", "Missing files")]
    public void For_KnownState_ReturnsReadableLabel(string state, string expected) =>
        Assert.Equal(expected, QBittorrentStateLabel.For(state));

    [Theory]
    [InlineData("not in client")]
    [InlineData("removed from MaM")]
    [InlineData("someFutureState")]
    public void For_UnknownState_ReturnsItUnchanged(string state) =>
        Assert.Equal(state, QBittorrentStateLabel.For(state));

    [Fact]
    public void For_Null_ReturnsNull() => Assert.Null(QBittorrentStateLabel.For(null));
}
