using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

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
    [InlineData("someFutureState")]
    public void For_UnknownState_ReturnsItUnchanged(string state) =>
        Assert.Equal(state, QBittorrentStateLabel.For(state));

    [Fact]
    public void For_Null_ReturnsNull() => Assert.Null(QBittorrentStateLabel.For(null));

    [Theory]
    [InlineData("uploading", "stalledUP")]
    [InlineData("stalledUP", "forcedUP")]
    [InlineData("pausedUP", "stoppedUP")]
    public void SameStage_StatesWithTheSameLabel_ReturnsTrue(string a, string b) =>
        Assert.True(QBittorrentStateLabel.SameStage(a, b));

    [Theory]
    [InlineData("uploading", "stoppedUP")]
    [InlineData("downloading", "stalledDL")]
    [InlineData("not in client", "uploading")]
    [InlineData(null, "uploading")]
    public void SameStage_StatesWithDifferentLabels_ReturnsFalse(string? a, string b) =>
        Assert.False(QBittorrentStateLabel.SameStage(a, b));

    [Theory]
    [InlineData("downloading", true)]
    [InlineData("stalledDL", true)]
    [InlineData("metaDL", true)]
    [InlineData("stoppedDL", true)]
    [InlineData("uploading", false)]
    [InlineData("stalledUP", false)]
    [InlineData("stoppedUP", false)]
    [InlineData("not in client", false)]
    [InlineData(null, false)]
    public void IsDownloadStage_OnlyForTorrentsStillDownloading(string? state, bool expected) =>
        Assert.Equal(expected, QBittorrentStateLabel.IsDownloadStage(state));
}
