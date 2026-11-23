namespace Fetchshelf.Clients.QBittorrent.Tests;

public class QBittorrentTorrentTests
{
    private static QBittorrentTorrent Build(double progress) => new()
    {
        Hash = "abc123",
        Name = "Some Book",
        SavePath = "/downloads/some-book",
        State = "downloading",
        Progress = progress,
    };

    [Fact]
    public void IsFinished_ProgressBelow100Percent_IsNotFinished()
    {
        Assert.False(Build(0.99).IsFinished);
    }

    [Fact]
    public void IsFinished_ProgressAt100Percent_IsFinished()
    {
        Assert.True(Build(1.0).IsFinished);
    }

    [Theory]
    [InlineData("MID=1151879,UID=260299", 1151879L)]
    [InlineData("UID=260299,MID=42", 42L)]
    [InlineData("", null)]
    [InlineData("Made with mktorrent", null)]
    [InlineData("AMID=7", null)]
    public void MamId_IsReadFromMamsComment(string comment, long? expected) =>
        Assert.Equal(expected, (Build(1.0) with { Comment = comment }).MamId);

    [Theory]
    [InlineData("https://t.myanonamouse.net/tracker.php/abc/announce", "", true)]
    [InlineData("https://myanonamouse.net/announce", "", true)]
    [InlineData("https://tracker.example/announce", "MID=42", false)]
    [InlineData("https://notmyanonamouse.net/announce", "", false)]
    [InlineData("", "MID=42", true)]
    [InlineData("", "Made with mktorrent", false)]
    public void IsMam_ByTracker_OrByMamsCommentWhenNoTrackerAnswered(string tracker, string comment, bool expected) =>
        Assert.Equal(expected, (Build(1.0) with { Tracker = tracker, Comment = comment }).IsMam);
}
