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
}
