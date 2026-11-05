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
}
